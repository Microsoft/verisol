// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license.
namespace SolToBoogie
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Diagnostics;
    using System.Globalization;
    using System.Linq;
    using System.Numerics;
    using System.Text.RegularExpressions;
    using BoogieAST;
    using SolidityAST;

    public class ProcedureTranslator : BasicASTVisitor
    {
        private TranslatorContext context;

        // used to declare local vars in a Boogie implementation
        private Dictionary<string, List<BoogieVariable>> boogieToLocalVarsMap;

        // current Boogie procedure being translated to
        private string currentBoogieProc = null;

        // does current procedure has inline assembly in it?
        private bool inlineAssemblyPresent = false;

        // update in the visitor for contract definition
        private ContractDefinition currentContract = null;
        // update in the visitor for function definition
        private FunctionDefinition currentFunction = null;

        // information about current file and linenumber
        private string currentSourceFile = null;
        private int currentSourceLine = -1;
        
        // store the Boogie call for modifier postlude
        private BoogieStmtList currentPostlude = null;

        // to generate inline attributes 
        private bool genInlineAttrsInBpl;

        // to collect contract invariants
        private Dictionary<string, List<BoogieExpr>> contractInvariants = null;

        private static void emitGasCheck(BoogieStmtList newBody)
        {
            BoogieStmtList thenBody = new BoogieStmtList();
            thenBody.AddStatement(new BoogieReturnCmd());

            newBody.AddStatement(new BoogieIfCmd(
                new BoogieBinaryOperation(BoogieBinaryOperation.Opcode.LT, new BoogieIdentifierExpr("gas"),
                    new BoogieLiteralExpr(0)), thenBody, null));
        }

        private void preTranslationAction(ASTNode node)
        {
            if (context.TranslateFlags.InstrumentGas)
            {
                // Some times solc will give a positive gas cost to some "weird" (i.e., outside a transaction or function) nodes.
                if (node.GasCost > 0 && currentStmtList != null)
                    // gas := gas - node.GasCost
                    currentStmtList.AddStatement(new BoogieAssignCmd(new BoogieIdentifierExpr("gas"), 
                        new BoogieBinaryOperation(BoogieBinaryOperation.Opcode.SUB, new BoogieIdentifierExpr("gas"), new BoogieLiteralExpr(node.GasCost))));

                if (context.TranslateFlags.ModelReverts)
                {
                    if (node is Continue)
                    {
                        emitGasCheck(currentStmtList);
                    }
                }
            }
        }

        public ProcedureTranslator(TranslatorContext context, bool _genInlineAttrsInBpl = true)
        {
            this.context = context;
            boogieToLocalVarsMap = new Dictionary<string, List<BoogieVariable>>();
            genInlineAttrsInBpl = _genInlineAttrsInBpl;
            contractInvariants = new Dictionary<string, List<BoogieExpr>>();
        }
        
        public override bool Visit(ContractDefinition node)
        {
            preTranslationAction(node);

            currentContract = node;

            if (currentContract.ContractKind == EnumContractKind.LIBRARY &&
                currentContract.Name.Equals("VeriSol"))
                return true;

            // generate default empty constructor if there is no constructor explicitly defined
            if (!context.IsConstructorDefined(node))
            {
                GenerateDefaultConstructor(node);
            }

            foreach (ASTNode child in node.Nodes)
            {
                if (child is VariableDeclaration varDecl)
                {
                    TranslateStateVarDeclaration(varDecl);
                }
                else if (child is StructDefinition structDefn)
                {
                    TranslateStructDefinition(structDefn);
                }
                else
                {
                    child.Accept(this);
                }
            }

            return false;
        }

        private void TranslateStructDefinition(StructDefinition structDefn)
        {
            foreach(var member in structDefn.Members)
            {
                VeriSolAssert(!member.TypeDescriptions.IsStruct(),
                    "Do no handle nested structs yet!");
                var type = TransUtils.GetBoogieTypeFromSolidityTypeName(member.TypeName);
                var mapType = new BoogieMapType(BoogieType.Ref, type);
                var mapName = member.Name + "_" + structDefn.CanonicalName;
                context.Program.AddDeclaration(new BoogieGlobalVariable(new BoogieTypedIdent(mapName, mapType)));
            }
        }

        private void TranslateStateVarDeclaration(VariableDeclaration varDecl)
        {
            VeriSolAssert(varDecl.StateVariable, $"{varDecl} is not a state variable");

            string name = TransUtils.GetCanonicalStateVariableName(varDecl, context);
            BoogieType type = TransUtils.GetBoogieTypeFromSolidityTypeName(varDecl.TypeName);
            BoogieMapType mapType = new BoogieMapType(BoogieType.Ref, type);

            // Issue a warning for intXX variables in case /useModularArithemtic option is used:
            if (context.TranslateFlags.UseModularArithmetic && varDecl.TypeDescriptions.IsInt())
            {
                Console.WriteLine($"Warning: signed integer arithmetic is not handled with /useModularArithmetic option");
            }

            if (varDecl.TypeName is Mapping)
            {
                context.Program.AddDeclaration(new BoogieGlobalVariable(new BoogieTypedIdent(name, mapType)));
            }
            else if (varDecl.TypeName is ArrayTypeName)
            {
                //array variables can be assigned
                context.Program.AddDeclaration(new BoogieGlobalVariable(new BoogieTypedIdent(name, mapType)));
            }
            else // other type of state variables
            {
                context.Program.AddDeclaration(new BoogieGlobalVariable(new BoogieTypedIdent(name, mapType)));
            }
        }

        public override bool Visit(EnumDefinition node)
        {
            preTranslationAction(node);
            // do nothing
            return false;
        }

        private BoogieCallCmd InstrumentForPrintingData(TypeDescription type, BoogieExpr value, string name)
        {
            // don't emit the instrumentation 
            if (context.TranslateFlags.NoDataValuesInfoFlag)
                return null;

            if (type.IsDynamicArray() || type.IsStaticArray())
                return null;

            if (type.IsAddress() || type.IsContract())
            {
                // Skipping dynamic and static array types:
                var callCmd = new BoogieCallCmd("boogie_si_record_sol2Bpl_ref", new List<BoogieExpr>() { value }, new List<BoogieIdentifierExpr>());
                callCmd.Attributes = new List<BoogieAttribute>();
                callCmd.Attributes.Add(new BoogieAttribute("cexpr", $"\"{name}\""));
                return callCmd;
            }
            else if (type.IsInt() || type.IsUint() || type.IsString() || type.IsBytes())
                // TypeString.StartsWith("uint") || type.TypeString.StartsWith("int") || type.TypeString.StartsWith("string ") || type.TypeString.StartsWith("bytes"))
            {
                var callCmd = new BoogieCallCmd("boogie_si_record_sol2Bpl_int", new List<BoogieExpr>() { value }, new List<BoogieIdentifierExpr>());
                callCmd.Attributes = new List<BoogieAttribute>();
                callCmd.Attributes.Add(new BoogieAttribute("cexpr", $"\"{name}\""));
                return callCmd;
            }
            else if (type.IsBool())
            {
                var callCmd = new BoogieCallCmd("boogie_si_record_sol2Bpl_bool", new List<BoogieExpr>() { value }, new List<BoogieIdentifierExpr>());
                callCmd.Attributes = new List<BoogieAttribute>();
                callCmd.Attributes.Add(new BoogieAttribute("cexpr", $"\"{name}\""));
                return callCmd;
            }

            return null;
        }


        private void PrintArguments(FunctionDefinition node, List<BoogieVariable> inParams, BoogieStmtList currentStmtList)
        {
            // Print dummy first parameter (as a delimeter for parsing corral.txt):
            TypeDescription addrType = new TypeDescription();
            addrType.TypeString = "bool";
            // There's no BoogieLiteralExpr that accepts ref type:
            //BoogieConstant nullConst = new BoogieConstant(new BoogieTypedIdent("null", BoogieType.Ref), true);
            var callCmd = InstrumentForPrintingData(addrType, new BoogieLiteralExpr(false), "_verisolFirstArg");
            if (callCmd != null)
            {
                currentStmtList.AddStatement(callCmd);
            }

            // Add default parameters "this", "msg.sender", "msg.value"
            addrType = new TypeDescription();
            addrType.TypeString = "address";
            callCmd = InstrumentForPrintingData(addrType, new BoogieIdentifierExpr(inParams[0].Name), "this");
            if (callCmd != null)
            {
                currentStmtList.AddStatement(callCmd);
            }
            callCmd = InstrumentForPrintingData(addrType, new BoogieIdentifierExpr(inParams[1].Name), "msg.sender");
            if (callCmd != null)
            {
                currentStmtList.AddStatement(callCmd);
            }
            var valType = new TypeDescription();
            valType.TypeString = "int";
            callCmd = InstrumentForPrintingData(valType, new BoogieIdentifierExpr(inParams[2].Name), "msg.value");
            if (callCmd != null)
            {
                currentStmtList.AddStatement(callCmd);
            }

            // when we call this for an implicit constructor, we don't have a node, which
            // implies there are no parameters
            if (node == null)
            {
                // Print dummy last parameter (as a delimeter for parsing corral.txt):
                addrType = new TypeDescription();
                addrType.TypeString = "bool";
                callCmd = InstrumentForPrintingData(addrType, new BoogieLiteralExpr(true), "_verisolLastArg");
                if (callCmd != null)
                {
                    currentStmtList.AddStatement(callCmd);
                }
                return;
            }
                
            foreach (VariableDeclaration param in node.Parameters.Parameters)
            {
                var parType = param.TypeDescriptions != null ? param.TypeDescriptions : null;
                int parIndex = node.Parameters.Parameters.IndexOf(param);
                BoogieVariable parVar = inParams[parIndex + 3];
                string parName = param.Name;
                var parExpr = new BoogieIdentifierExpr(parVar.Name);
                callCmd = InstrumentForPrintingData(parType, parExpr, parName);
                if (callCmd != null)
                {
                    currentStmtList.AddStatement(callCmd);
                }
            }
            // Print dummy last parameter (as a delimeter for parsing corral.txt):
            addrType = new TypeDescription();
            addrType.TypeString = "bool";
            callCmd = InstrumentForPrintingData(addrType, new BoogieLiteralExpr(true), "_verisolLastArg");
            if (callCmd != null)
            {
                currentStmtList.AddStatement(callCmd);
            }
        }
        public override bool Visit(FunctionDefinition node)
        {
            preTranslationAction(node);
            // VeriSolAssert(node.IsConstructor || node.Modifiers.Count <= 1, "Multiple Modifiers are not supported yet");
            VeriSolAssert(currentContract != null);

            currentFunction = node;

            // procedure name
            string procName = node.Name + "_" + currentContract.Name;

            if (node.IsConstructor)
            {
                procName += "_NoBaseCtor";
            }
            currentBoogieProc = procName;

            // input parameters
            List<BoogieVariable> inParams = TransUtils.GetDefaultInParams();
            // initialize statement list to include assumption about parameter types
            currentStmtList = new BoogieStmtList();
            // get all formal input parameters
            node.Parameters.Accept(this);
            inParams.AddRange(currentParamList);

            // Print function argument values to corral.txt for counterexample:
            PrintArguments(node, inParams, currentStmtList);

            // output parameters
            isReturnParameterList = true;
            node.ReturnParameters.Accept(this);
            isReturnParameterList = false;
            List<BoogieVariable> outParams = currentParamList;

            var assumesForParamsAndReturn = currentStmtList;
            currentStmtList = new BoogieStmtList();

            // attributes
            List<BoogieAttribute> attributes = new List<BoogieAttribute>();
            if ((node.Visibility == EnumVisibility.PUBLIC || node.Visibility == EnumVisibility.EXTERNAL)
                && !node.IsConstructor
                && !node.IsFallback) //don't expose fallback for calling directly
            {
                attributes.Add(new BoogieAttribute("public"));
            }
            // generate inline attribute for a function only when /noInlineAttrs is specified
            if (genInlineAttrsInBpl)
                attributes.Add(new BoogieAttribute("inline", 1));

            if (currentContract.ContractKind == EnumContractKind.LIBRARY &&
                currentContract.Name.Equals("VeriSol"))
            {
                return false;
            }
            // we add any pre/post conditions after analyzing the boy later
            BoogieProcedure procedure = new BoogieProcedure(procName, inParams, outParams, attributes);
            context.Program.AddDeclaration(procedure);

            // could be just a declaration
            if (!node.Implemented)
            {
                return false;
            }

            // skip if it in ignored set
            if (context.IsMethodInIgnoredSet(node, currentContract))
            {
                Console.WriteLine($"Warning!: Ignoring method {node.Name} in contract {currentContract.Name} specified using /ignoreMethod:");
            }
            else
            {
                // local variables and function body
                // TODO: move to earlier
                boogieToLocalVarsMap[currentBoogieProc] = new List<BoogieVariable>();

                // TODO: each local array variable should be distinct and 0 initialized

                BoogieStmtList procBody = new BoogieStmtList();
                currentPostlude = new BoogieStmtList();


                // Add possible assume statements from parameters
                procBody.AppendStmtList(assumesForParamsAndReturn);

                // if payable, then modify the balance
                if (node.StateMutability == EnumStateMutability.PAYABLE)
                {
                    procBody.AddStatement(new BoogieCommentCmd("---- Logic for payable function START "));
                    var balnSender = new BoogieMapSelect(new BoogieIdentifierExpr("Balance"), new BoogieIdentifierExpr("msgsender_MSG"));
                    var balnThis = new BoogieMapSelect(new BoogieIdentifierExpr("Balance"), new BoogieIdentifierExpr("this"));
                    var msgVal = new BoogieIdentifierExpr("msgvalue_MSG");
                    //assume Balance[msg.sender] >= msg.value
                    procBody.AddStatement(new BoogieAssumeCmd(new BoogieBinaryOperation(BoogieBinaryOperation.Opcode.GE, balnSender, msgVal)));
                    //balance[msg.sender] = balance[msg.sender] - msg.value
                    procBody.AddStatement(new BoogieAssignCmd(balnSender, new BoogieBinaryOperation(BoogieBinaryOperation.Opcode.SUB, balnSender, msgVal)));
                    //balance[this] = balance[this] + msg.value
                    procBody.AddStatement(new BoogieAssignCmd(balnThis, new BoogieBinaryOperation(BoogieBinaryOperation.Opcode.ADD, balnThis, msgVal)));
                    procBody.AddStatement(new BoogieCommentCmd("---- Logic for payable function END "));
                }

                // if (node.Modifiers.Count == 1)
                for (int i = 0; i < node.Modifiers.Count; ++i)
                {
                    var outVars = new List<BoogieIdentifierExpr>();
                    
                    // insert call to modifier prelude
                    if (context.ModifierToBoogiePreImpl.ContainsKey(node.Modifiers[i].ModifierName.Name))
                    {
                        // local variables declared in the prelude of a modifier (before _ ) becomes out variables and then in parameters of postlude
                        foreach (var localVar in context.ModifierToPreludeLocalVars[node.Modifiers[i].ModifierName.ToString()])
                        {
                            var outVar = new BoogieLocalVariable(new BoogieTypedIdent("__mod_out_" + localVar.Name, localVar.TypedIdent.Type));
                            boogieToLocalVarsMap[currentBoogieProc].Add(outVar);
                            outVars.Add(new BoogieIdentifierExpr(outVar.Name));
                        }

                        List<BoogieExpr> arguments = TransUtils.GetDefaultArguments();
                        if (node.Modifiers[i].Arguments != null)
                            arguments.AddRange(node.Modifiers[i].Arguments.ConvertAll(TranslateExpr));
                        string callee = node.Modifiers[i].ModifierName.ToString() + "_pre";
                        var callCmd = new BoogieCallCmd(callee, arguments, outVars);
                        procBody.AddStatement(callCmd);
                    }

                    // insert call to modifier postlude
                    if (context.ModifierToBoogiePostImpl.ContainsKey(node.Modifiers[i].ModifierName.Name))
                    {
                        List<BoogieExpr> arguments = TransUtils.GetDefaultArguments();
                        if (node.Modifiers[i].Arguments != null)
                            arguments.AddRange(node.Modifiers[i].Arguments.ConvertAll(TranslateExpr));
                        arguments.AddRange(outVars.Select(x => new BoogieIdentifierExpr(x.Name)));
                        string callee = node.Modifiers[i].ModifierName.ToString() + "_post";
                        var callCmd = new BoogieCallCmd(callee, arguments, null);
                        currentPostlude.AddStatement(callCmd);
                    }
                }

                try
                {
                    procBody.AppendStmtList(TranslateStatement(node.Body));
                }
                catch (NotImplementedException e)
                {
                    // In case of an inline assembly in the body of the function, do not emit procedure body (aka implementation):
                    if (e.Message.Equals("inline assembly"))
                    {
                        inlineAssemblyPresent = true;
                    }
                    else
                    {
                        throw e;
                    }
                }             

                // add modifier postlude call if function body has no return
                if (currentPostlude != null)
                {
                    procBody.AppendStmtList(currentPostlude);
                    currentPostlude = null;
                }

                // initialization statements
                if (node.IsConstructor)
                {
                    BoogieStmtList oldCurrentStmtList = currentStmtList;
                    currentStmtList = new BoogieStmtList();
                    BoogieStmtList initStmts;
                    GenerateInitializationStmts(currentContract);
                    initStmts = currentStmtList;
                    currentStmtList = oldCurrentStmtList;
                    initStmts.AppendStmtList(procBody);
                    procBody = initStmts;
                }

                // is it a VeriSol Contract Invariant function?
                List<BoogieExpr> contractInvs = null;
                if (IsVeriSolContractInvariantFunction(node, procBody, out contractInvs))
                {
                    //add contract invs as loop invariants to outer loop
                    VeriSolAssert(!contractInvariants.ContainsKey(currentContract.Name), $"More than one function defining the contract invariant for contract {currentContract.Name}");
                    contractInvariants[currentContract.Name] = contractInvs;
                }
                else
                {
                    //extract the specifications from within the body
                    BoogieStmtList procBodyWoRequires, procBodyWoEnsures, procBodyWoModifies;
                    var preconditions = ExtractSpecifications("Requires_VeriSol", procBody, out procBodyWoRequires);
                    var postconditions = ExtractSpecifications("Ensures_VeriSol", procBodyWoRequires, out procBodyWoEnsures);
                    var modifies = ExtractSpecifications("Modifies_VeriSol", procBodyWoEnsures, out procBodyWoModifies);

                    procedure.AddPreConditions(preconditions);
                    procedure.AddPostConditions(postconditions);
                    procedure.AddPostConditions(modifies);
                    List<BoogieVariable> localVars = boogieToLocalVarsMap[currentBoogieProc];
                    if (!inlineAssemblyPresent)
                    {
                        BoogieImplementation impelementation = new BoogieImplementation(procName, inParams, outParams, localVars, procBodyWoModifies);
                        context.Program.AddDeclaration(impelementation);
                    }
                    else
                    {
                        inlineAssemblyPresent = false;
                    }
                }
            }

            // generate real constructors
            if (node.IsConstructor)
            {
                GenerateConstructorWithBaseCalls(node, inParams);
            }

            return false;
        }

        private bool IsVeriSolContractInvariantFunction(FunctionDefinition node, BoogieStmtList procBody, out List<BoogieExpr> contractInvs)
        {
            contractInvs = null;
            if (node.Visibility != EnumVisibility.PRIVATE ||
                node.StateMutability != EnumStateMutability.VIEW ||
                node.Parameters.Parameters.Count != 0)
            {
                return false;
            }

            contractInvs = ExtractContractInvariants(procBody);
            return contractInvs.Count > 0;
        }

        public override bool Visit(ModifierDefinition node)
        {
            preTranslationAction(node);
            currentBoogieProc = node.Name + "_pre";
            boogieToLocalVarsMap[currentBoogieProc] = new List<BoogieVariable>();

            Block body = node.Body;
            BoogieStmtList prelude = new BoogieStmtList();
            BoogieStmtList postlude = new BoogieStmtList();

            bool translatingPre = true;
            bool hasPre = false;
            bool hasPost = false;

            foreach (Statement statement in body.Statements)
            {
                if (statement is VariableDeclarationStatement)
                {
                    // VeriSolAssert(false, "locals within modifiers not supported");
                }
                if (statement is PlaceholderStatement)
                {
                    translatingPre = false;
                    // add __out_mod_x := x, for any local explicitly declared in the prelude
                    foreach(var localDeclared in context.ModifierToPreludeLocalVars[node.Name])
                    {
                        var lhsExpr = new BoogieIdentifierExpr("__out_mod_" + localDeclared.Name);
                        var rhsExpr = new BoogieIdentifierExpr(localDeclared.Name);
                        prelude.AddStatement(new BoogieAssignCmd(lhsExpr, rhsExpr));
                    }
                    currentBoogieProc = node.Name + "_post";
                    boogieToLocalVarsMap[currentBoogieProc] = new List<BoogieVariable>();
                    continue;
                }
                BoogieStmtList stmtList = TranslateStatement(statement);
                if (translatingPre)
                {
                    prelude.AppendStmtList(stmtList);
                    hasPre = true;
                }
                else
                {
                    postlude.AppendStmtList(stmtList);
                    hasPost = true;
                }
            }

            // we are going to make any locals declared in prelude visible to postlude by making htem as output variables
            // and making them input to the postlude
            if (hasPre)
            {
                // removig this removes local declaration of temporaries introduced in translation
                context.ModifierToBoogiePreImpl[node.Name].LocalVars = boogieToLocalVarsMap[node.Name + "_pre"];
                // context.ModifierToBoogiePreImpl[node.Name].LocalVars = new List<BoogieVariable>();
                context.ModifierToBoogiePreImpl[node.Name].StructuredStmts = prelude;
            }
            if (hasPost)
            {
                context.ModifierToBoogiePostImpl[node.Name].LocalVars = boogieToLocalVarsMap[node.Name + "_post"];
                context.ModifierToBoogiePostImpl[node.Name].StructuredStmts = postlude;
            }
            return false;
        }

        // generate the initialization statements for state variables
        // assume message sender is not null
        // assign null to other address variables
        private BoogieStmtList GenerateInitializationStmts(ContractDefinition contract)
        {
            currentStmtList = new BoogieStmtList();
            currentStmtList.AddStatement(new BoogieCommentCmd("start of initialization"));

            // assume msgsender_MSG != null;
            BoogieExpr assumeLhs = new BoogieIdentifierExpr("msgsender_MSG");
            BoogieExpr assumeExpr = new BoogieBinaryOperation(BoogieBinaryOperation.Opcode.NEQ, assumeLhs, new BoogieIdentifierExpr("null"));
            BoogieAssumeCmd assumeCmd = new BoogieAssumeCmd(assumeExpr);
            currentStmtList.AddStatement(assumeCmd);

            BoogieAssignCmd balanceInit =
                new BoogieAssignCmd(
                    new BoogieMapSelect(new BoogieIdentifierExpr("Balance"), new BoogieIdentifierExpr("this")),
                    new BoogieLiteralExpr(0));
            currentStmtList.AddStatement(balanceInit);

            foreach (VariableDeclaration varDecl in context.GetStateVarsByContract(contract))
            {
                // assign null to other address variables
                if (varDecl.TypeName is ElementaryTypeName elementaryType)
                {
                    GenerateInitializationForElementaryTypes(varDecl, elementaryType);
                }

                //// false/0 initialize mappings, arrays and structs (one level)
                if (varDecl.TypeName is Mapping mapping)
                {
                    GenerateInitializationForMappingStateVar(varDecl, mapping);
                }
                else if (varDecl.TypeName is ArrayTypeName array)
                {
                    GenerateInitializationForArrayStateVar(varDecl, array);
                }
                else if (varDecl.TypeName is UserDefinedTypeName structOrContract)
                {
                    if (varDecl.TypeDescriptions.IsStruct())
                        GenerateInitializationForStructStateVar(varDecl, structOrContract);
                }
            }


            currentStmtList.AddStatement(new BoogieCommentCmd("end of initialization"));

            // TODO: add the initializations outside of constructors

            return currentStmtList;
        }

        private void GenerateInitializationForArrayStateVar(VariableDeclaration varDecl, ArrayTypeName array)
        {
            // Issue a warning for intXX type in case /useModularArithemtic option is used:
            if (context.TranslateFlags.UseModularArithmetic && array.BaseType.ToString().StartsWith("int"))
            {
                Console.WriteLine($"Warning: signed integer arithmetic is not handled with /useModularArithmetic option");
            }

            BoogieMapSelect lhsMap = CreateDistinctArrayMappingAddress(currentStmtList, varDecl);

            // lets also initialize the array Lengths (only for Arrays declared in this class)
            var lengthMapSelect = new BoogieMapSelect(new BoogieIdentifierExpr("Length"), lhsMap);
            var lengthExpr = array.Length == null ? new BoogieLiteralExpr(BigInteger.Zero) : TranslateExpr(array.Length);
            // var lengthEqualsZero = new BoogieBinaryOperation(BoogieBinaryOperation.Opcode.EQ, lengthMapSelect, new BoogieLiteralExpr(0));
            var lengthConstraint = new BoogieBinaryOperation(BoogieBinaryOperation.Opcode.EQ, lengthMapSelect, lengthExpr);
            currentStmtList.AddStatement(new BoogieAssumeCmd(lengthConstraint));
        }

        private void GenerateInitializationForStructStateVar(VariableDeclaration varDecl, UserDefinedTypeName structVar)
        {
            // define a local variable to generate a fresh constant
            BoogieLocalVariable tmpVar = new BoogieLocalVariable(context.MakeFreshTypedIdent(BoogieType.Ref));
            boogieToLocalVarsMap[currentBoogieProc].Add(tmpVar);
            BoogieIdentifierExpr tmpVarIdentExpr = new BoogieIdentifierExpr(tmpVar.Name);

            currentStmtList.AddStatement(new BoogieCommentCmd($"Make struct variables distinct for {varDecl.Name}"));
            var lhs = new BoogieIdentifierExpr(TransUtils.GetCanonicalStateVariableName(varDecl, context));
            var lhsMap = new BoogieMapSelect(lhs, new BoogieIdentifierExpr("this"));
            currentStmtList.AddStatement(new BoogieCallCmd(
                "FreshRefGenerator",
                new List<BoogieExpr>(),
                new List<BoogieIdentifierExpr>() {
                            tmpVarIdentExpr
                }
                ));

            currentStmtList.AddStatement(new BoogieAssignCmd(lhsMap, tmpVarIdentExpr));
        }

        private BoogieFuncCallExpr GetCallExprForZeroInit(BoogieType key, BoogieType value)
        {
            var keyStr = char.ToUpper(key.ToString()[0]) + key.ToString().Substring(1);
            var valStr = char.ToUpper(value.ToString()[0]) + value.ToString().Substring(1);
            return new BoogieFuncCallExpr("zero" + keyStr + valStr + "Arr", new List<BoogieExpr>());
        }

        private BoogieExpr getSumArray()
        {
            return new BoogieIdentifierExpr("sum");
        }

        private BoogieExpr getSumAccess(BoogieExpr key)
        {
            return new BoogieMapSelect(getSumArray(), key);
        }
        
        public BoogieAssignCmd adjustSum(BoogieBinaryOperation.Opcode op, BoogieExpr sumInd, BoogieExpr amt)
        {
            /*BoogieExpr arrExpr = accessExpr.BaseExpr;
            BoogieExpr arrExpr1 = TranslateExpr(baseExpression);*/
            BoogieExpr sumAccess = getSumAccess(sumInd);
            BoogieExpr sumSub = new BoogieBinaryOperation(op, sumAccess, amt);
            return new BoogieAssignCmd(sumAccess, sumSub);
        }
        
        private void GenerateInitializationForMappingStateVar(VariableDeclaration varDecl, Mapping mapping)
        {
            BoogieMapSelect lhsMap = CreateDistinctArrayMappingAddress(currentStmtList, varDecl);

            //nested arrays (only 1 level for now)
            if (mapping.ValueType is ArrayTypeName array)
            {
                Console.WriteLine($"Warning: A mapping with nested array {varDecl.Name} of valuetype {mapping.ValueType.ToString()}");

                InitializeNestedArrayMappingStateVar(varDecl, mapping);
            }
            else if (mapping.ValueType is Mapping mappingNested)
            {
                InitializeNestedArrayMappingStateVar(varDecl, mapping);
                // TODO: add the initialization of m[i][j]
            }
            else if (mapping.ValueType is UserDefinedTypeName userTypeName ||
                mapping.ValueType.ToString().Equals("address") ||
                mapping.ValueType.ToString().Equals("address payable") ||
                mapping.ValueType.ToString().StartsWith("bytes")
                )
            {
                currentStmtList.AddStatement(new BoogieCommentCmd($"Initialize address/contract mapping {varDecl.Name}"));

                BoogieType mapKeyType;
                BoogieMapSelect lhs;
                GetBoogieTypesFromMapping(varDecl, mapping, out mapKeyType, out lhs);
                if (!context.TranslateFlags.QuantFreeAllocs)
                {
                    var qVar = QVarGenerator.NewQVar(0, 0);
                    BoogieExpr zeroExpr = new BoogieIdentifierExpr("null");

                    if (mapping.ValueType.ToString().StartsWith("bytes"))
                        zeroExpr = new BoogieLiteralExpr(BigInteger.Zero);

                    var bodyExpr = new BoogieBinaryOperation(
                        BoogieBinaryOperation.Opcode.EQ,
                        new BoogieMapSelect(lhs, qVar),
                        zeroExpr);
                    var qExpr = new BoogieQuantifiedExpr(true, new List<BoogieIdentifierExpr>() {qVar},
                        new List<BoogieType>() {mapKeyType}, bodyExpr);
                    currentStmtList.AddStatement(new BoogieAssumeCmd(qExpr));
                }
                else
                {
                    if (mapping.ValueType.ToString().StartsWith("bytes"))
                        currentStmtList.AddStatement(new BoogieAssignCmd(lhs,
                            GetCallExprForZeroInit(mapKeyType, BoogieType.Int)));
                    else
                        currentStmtList.AddStatement(new BoogieAssignCmd(lhs, GetCallExprForZeroInit(mapKeyType, BoogieType.Ref)));
                }
            }
            else if (mapping.ValueType.ToString().Equals("bool"))
            {
                currentStmtList.AddStatement(new BoogieCommentCmd($"Initialize Boolean mapping {varDecl.Name}"));

                BoogieType mapKeyType;
                BoogieMapSelect lhs;
                GetBoogieTypesFromMapping(varDecl, mapping, out mapKeyType, out lhs);
                if (!context.TranslateFlags.QuantFreeAllocs)
                {
                    var qVar = QVarGenerator.NewQVar(0, 0);
                    var bodyExpr =
                        new BoogieUnaryOperation(BoogieUnaryOperation.Opcode.NOT, new BoogieMapSelect(lhs, qVar));
                    var qExpr = new BoogieQuantifiedExpr(true, new List<BoogieIdentifierExpr>() {qVar},
                        new List<BoogieType>() {mapKeyType}, bodyExpr);
                    currentStmtList.AddStatement(new BoogieAssumeCmd(qExpr));
                }
                else
                {
                    currentStmtList.AddStatement(new BoogieAssignCmd(lhs, GetCallExprForZeroInit(mapKeyType, BoogieType.Bool)));
                }
            }
            // TODO: Cleanup, StartsWith("uint") can include uint[12] as well. 
            else if (mapping.ValueType.ToString().StartsWith("uint") ||
                mapping.ValueType.ToString().StartsWith("int"))
            {
                // Issue a warning for intXX type in case /useModularArithemtic option is used:
                if (context.TranslateFlags.UseModularArithmetic && mapping.ValueType.ToString().StartsWith("int"))
                {
                    Console.WriteLine($"Warning: signed integer arithmetic is not handled with /useModularArithmetic option");
                }

                currentStmtList.AddStatement(new BoogieCommentCmd($"Initialize Integer mapping {varDecl.Name}"));

                BoogieType mapKeyType;
                BoogieMapSelect lhs;
                GetBoogieTypesFromMapping(varDecl, mapping, out mapKeyType, out lhs);
                if (!context.TranslateFlags.QuantFreeAllocs)
                {
                    var qVar = QVarGenerator.NewQVar(0, 0);
                    var bodyExpr = new BoogieBinaryOperation(
                        BoogieBinaryOperation.Opcode.EQ,
                        new BoogieMapSelect(lhs, qVar),
                        new BoogieLiteralExpr(0));
                    var qExpr = new BoogieQuantifiedExpr(true, new List<BoogieIdentifierExpr>() {qVar},
                        new List<BoogieType>() {mapKeyType}, bodyExpr);
                    currentStmtList.AddStatement(new BoogieAssumeCmd(qExpr));
                }
                else
                {
                    currentStmtList.AddStatement(new BoogieAssignCmd(lhs, GetCallExprForZeroInit(mapKeyType, BoogieType.Int)));
                }

                if (context.TranslateFlags.InstrumentSums)
                {
                    currentStmtList.AddStatement(new BoogieAssignCmd(getSumAccess(lhsMap), new BoogieLiteralExpr(BigInteger.Zero)));
                }
            }
            else
            {
                Console.WriteLine($"Warning: A mapping with complex value type {varDecl.Name} of valuetype {mapping.ValueType.ToString()}");
            }
        }

        private void GenerateInitializationForElementaryTypes(VariableDeclaration varDecl, ElementaryTypeName elementaryType)
        {
            if (elementaryType.TypeDescriptions.TypeString.Equals("address") || elementaryType.TypeDescriptions.TypeString.Equals("address payable"))
            {
                string varName = TransUtils.GetCanonicalStateVariableName(varDecl, context);
                BoogieExpr lhs = new BoogieMapSelect(new BoogieIdentifierExpr(varName), new BoogieIdentifierExpr("this"));
                BoogieExpr rhs = new BoogieIdentifierExpr("null");
                if (varDecl.Value != null)
                {
                    VeriSolAssert(varDecl.Value is FunctionCall, $"We only support initialization of hte form address x = address(...);, found {varDecl.Value.ToString()}");
                    rhs = TranslateExpr(varDecl.Value);
                }
                BoogieAssignCmd assignCmd = new BoogieAssignCmd(lhs, rhs);
                currentStmtList.AddStatement(assignCmd);
            }
            else if (elementaryType.TypeDescriptions.TypeString.Equals("bool"))
            {
                string varName = TransUtils.GetCanonicalStateVariableName(varDecl, context);
                BoogieExpr lhs = new BoogieMapSelect(new BoogieIdentifierExpr(varName), new BoogieIdentifierExpr("this"));
                bool value = false;
                if (varDecl.Value != null && varDecl.Value.ToString() == "true")
                {
                    value = true;
                }
                BoogieAssignCmd assignCmd = new BoogieAssignCmd(lhs, new BoogieLiteralExpr(value));
                currentStmtList.AddStatement(assignCmd);
            }
            else if (elementaryType.TypeDescriptions.TypeString.Equals("string"))
            {
                string x = "";
                if (varDecl.Value != null)
                {
                    x = varDecl.Value.ToString();
                    x = x.Substring(1, x.Length - 2);  //to strip off the single quotations
                }
                int hashCode = x.GetHashCode();
                BigInteger num = new BigInteger(hashCode);
                string varName = TransUtils.GetCanonicalStateVariableName(varDecl, context);
                BoogieExpr lhs = new BoogieMapSelect(new BoogieIdentifierExpr(varName), new BoogieIdentifierExpr("this"));
                BoogieAssignCmd assignCmd = new BoogieAssignCmd(lhs, new BoogieLiteralExpr(num));
                currentStmtList.AddStatement(assignCmd);
            }
            else //it is integer valued
            {
                // Issue a warning for intXX variables in case /useModularArithemtic option is used:
                if (context.TranslateFlags.UseModularArithmetic && varDecl.TypeDescriptions.IsInt())
                {
                    Console.WriteLine($"Warning: signed integer arithmetic is not handled with /useModularArithmetic option");
                }

                string varName = TransUtils.GetCanonicalStateVariableName(varDecl, context);
                BoogieExpr lhs = new BoogieMapSelect(new BoogieIdentifierExpr(varName), new BoogieIdentifierExpr("this"));
                var bigInt = (BoogieExpr)new BoogieLiteralExpr(BigInteger.Zero);
                if (varDecl.Value != null)
                {
                    bigInt = TranslateExpr(varDecl.Value); //TODO: any complex expression will crash
                }
                BoogieAssignCmd assignCmd = new BoogieAssignCmd(lhs, bigInt);
                currentStmtList.AddStatement(assignCmd);
            }
        }

        private void InitializeNestedArrayMappingStateVar(VariableDeclaration varDecl, Mapping mapping)
        {
            currentStmtList.AddStatement(new BoogieCommentCmd($"Initialize length of 1-level nested array in {varDecl.Name}"));
            // Issue with inferring Array[] expressions in GetBoogieTypesFromMapping (TODO: use GetBoogieTypesFromMapping after fix)
            var mapKeyType = MapArrayHelper.InferExprTypeFromTypeString(mapping.KeyType.TypeDescriptions.ToString());
            string mapName = MapArrayHelper.GetMemoryMapName(mapKeyType, BoogieType.Ref);
            string varName = TransUtils.GetCanonicalStateVariableName(varDecl, context);
            var varExpr = new BoogieIdentifierExpr(varName);
            //lhs is Mem_t_ref[x[this]]
            var lhs0 = new BoogieMapSelect(new BoogieIdentifierExpr(mapName),
                new BoogieMapSelect(varExpr, new BoogieIdentifierExpr("this")));

            var qVar1 = QVarGenerator.NewQVar(0, 0);
            //lhs is Mem_t_ref[x[this]][i]
            var lhs1 = new BoogieMapSelect(lhs0, qVar1);

            if (!context.TranslateFlags.LazyNestedAlloc)
            {
                //Length[Mem_t_ref[x[this]][i]] == 0
                var bodyExpr = new BoogieBinaryOperation(
                    BoogieBinaryOperation.Opcode.EQ,
                    new BoogieMapSelect(new BoogieIdentifierExpr("Length"), lhs1),
                    new BoogieLiteralExpr(0));
                var qExpr = new BoogieQuantifiedExpr(true, new List<BoogieIdentifierExpr>() {qVar1},
                    new List<BoogieType>() {mapKeyType}, bodyExpr);
                currentStmtList.AddStatement(new BoogieAssumeCmd(qExpr));

                //Nested arrays are disjoint and disjoint from other addresses
                BoogieExpr allocExpr = new BoogieMapSelect(new BoogieIdentifierExpr("Alloc"), lhs1);
                var negAllocExpr = new BoogieUnaryOperation(BoogieUnaryOperation.Opcode.NOT, allocExpr);
                var negAllocQExpr = new BoogieQuantifiedExpr(true, new List<BoogieIdentifierExpr>() {qVar1},
                    new List<BoogieType>() {mapKeyType}, negAllocExpr);
                //assume forall i !Alloc[M_t_ref[x[this]][i]]
                currentStmtList.AddStatement(new BoogieAssumeCmd(negAllocQExpr));
                //call HavocAllocMany()
                currentStmtList.AddStatement(new BoogieCallCmd("HavocAllocMany", new List<BoogieExpr>(),
                    new List<BoogieIdentifierExpr>()));
                //assume forall i. Alloc[M_t_ref[x[this]][i]]
                var allocQExpr = new BoogieQuantifiedExpr(true, new List<BoogieIdentifierExpr>() {qVar1},
                    new List<BoogieType>() {mapKeyType}, allocExpr);
                currentStmtList.AddStatement(new BoogieAssumeCmd(allocQExpr));

                //Two different keys/indices within the same array are distinct
                //forall i, j: i != j ==> M_t_ref[x[this]][i] != M_t_ref[x[this]][j]
                var qVar2 = QVarGenerator.NewQVar(0, 1);
                var lhs2 = new BoogieMapSelect(lhs0, qVar2);
                var distinctQVars = new BoogieBinaryOperation(BoogieBinaryOperation.Opcode.EQ, qVar1, qVar2);
                var distinctLhs = new BoogieBinaryOperation(BoogieBinaryOperation.Opcode.NEQ, lhs1, lhs2);
                var triggers = new List<BoogieExpr>() {lhs1, lhs2};

                var neqExpr = new BoogieBinaryOperation(BoogieBinaryOperation.Opcode.OR, distinctQVars, distinctLhs);
                var distinctQExpr = new BoogieQuantifiedExpr(true, new List<BoogieIdentifierExpr>() {qVar1, qVar2},
                    new List<BoogieType>() {mapKeyType, mapKeyType}, neqExpr, triggers);
                currentStmtList.AddStatement(new BoogieAssumeCmd(distinctQExpr));
            }
            else
            {
                if (context.TranslateFlags.QuantFreeAllocs)
                {
                    currentStmtList.AddStatement(new BoogieAssignCmd(lhs0, GetCallExprForZeroInit(mapKeyType, BoogieType.Ref)));
                }
                else
                {
                    var bodyExpr = new BoogieBinaryOperation(
                        BoogieBinaryOperation.Opcode.EQ, 
                        lhs1,
                        new BoogieIdentifierExpr("null"));
                    var qExpr = new BoogieQuantifiedExpr(true, new List<BoogieIdentifierExpr>() {qVar1},
                        new List<BoogieType>() {mapKeyType}, bodyExpr);
                    currentStmtList.AddStatement(new BoogieAssumeCmd(qExpr));
                }
            }
        }

        private BoogieMapSelect CreateDistinctArrayMappingAddress(BoogieStmtList stmtList, VariableDeclaration varDecl)
        {
            // define a local variable to generate a fresh constant
            BoogieLocalVariable tmpVar = new BoogieLocalVariable(context.MakeFreshTypedIdent(BoogieType.Ref));
            boogieToLocalVarsMap[currentBoogieProc].Add(tmpVar);
            BoogieIdentifierExpr tmpVarIdentExpr = new BoogieIdentifierExpr(tmpVar.Name);

            stmtList.AddStatement(new BoogieCommentCmd($"Make array/mapping vars distinct for {varDecl.Name}"));
            var lhs = new BoogieIdentifierExpr(TransUtils.GetCanonicalStateVariableName(varDecl, context));
            var lhsMap = new BoogieMapSelect(lhs, new BoogieIdentifierExpr("this"));
            stmtList.AddStatement(new BoogieCallCmd(
                "FreshRefGenerator",
                new List<BoogieExpr>(),
                new List<BoogieIdentifierExpr>() {
                            tmpVarIdentExpr
                }
                ));

            stmtList.AddStatement(new BoogieAssignCmd(lhsMap, tmpVarIdentExpr));
            return lhsMap;
        }

        private void GetBoogieTypesFromMapping(VariableDeclaration varDecl, Mapping mapping, out BoogieType mapKeyType, out BoogieMapSelect lhs)
        {
            mapKeyType = MapArrayHelper.InferExprTypeFromTypeString(mapping.KeyType.TypeDescriptions.ToString());
            var mapValueTypeString = mapping.ValueType is UserDefinedTypeName ?
                ((UserDefinedTypeName)mapping.ValueType).TypeDescriptions.ToString() :
                mapping.ValueType.ToString();
            // needed as a mapping(int => contract A) only has "A" as the valueType.ToSTring()
            var mapValueType = MapArrayHelper.InferExprTypeFromTypeString(mapValueTypeString);
            string mapName = MapArrayHelper.GetMemoryMapName(mapKeyType, mapValueType);

            string varName = TransUtils.GetCanonicalStateVariableName(varDecl, context);
            var varExpr = new BoogieIdentifierExpr(varName);
            lhs = new BoogieMapSelect(new BoogieIdentifierExpr(mapName),
                new BoogieMapSelect(varExpr, new BoogieIdentifierExpr("this")));
        }

        // generate the default empty constructors, including an internal one without base ctors, and an actual one with base ctors
        // TODO: refactor this code with the code to generate constructor code when definition is present
        private void GenerateDefaultConstructor(ContractDefinition contract)
        {
            // generate the internal one without base constructors
            string procName = contract.Name + "_" + contract.Name + "_NoBaseCtor";
            currentBoogieProc = procName;
            if (!boogieToLocalVarsMap.ContainsKey(currentBoogieProc))
            {
                boogieToLocalVarsMap[currentBoogieProc] = new List<BoogieVariable>();
            }
            
            List<BoogieVariable> inParams = TransUtils.GetDefaultInParams();
            List<BoogieVariable> outParams = new List<BoogieVariable>();
            List<BoogieAttribute> attributes = new List<BoogieAttribute>();
            if (context.TranslateFlags.GenerateInlineAttributes)
            {
                attributes.Add(new BoogieAttribute("inline", 1));
            };
            BoogieProcedure procedure = new BoogieProcedure(procName, inParams, outParams, attributes);
            context.Program.AddDeclaration(procedure);

            BoogieStmtList procBody = GenerateInitializationStmts(contract);
            List<BoogieVariable> localVars = boogieToLocalVarsMap[currentBoogieProc];
            BoogieImplementation implementation = new BoogieImplementation(procName, inParams, outParams, localVars, procBody);
            context.Program.AddDeclaration(implementation);

            // generate the actual one with base constructors
            string ctorName = contract.Name + "_" + contract.Name;
            BoogieProcedure ctorWithBaseCalls = new BoogieProcedure(ctorName, inParams, outParams, attributes);
            context.Program.AddDeclaration(ctorWithBaseCalls);

            List<BoogieVariable> ctorLocalVars = new List<BoogieVariable>();
            BoogieStmtList ctorBody = new BoogieStmtList();

            // add the printing for argument
            // Print function argument values to corral.txt for counterexample:
            PrintArguments(null, inParams, ctorBody);

            // print sourcefile, and line of the contract start for
            // forcing Corral to print values consistently
            if (!context.TranslateFlags.NoSourceLineInfoFlag)
                ctorBody.AddStatement(InstrumentSourceFileAndLineInfo(contract));

            List<int> baseContractIds = new List<int>(contract.LinearizedBaseContracts);
            baseContractIds.Reverse();
            foreach (int id in baseContractIds)
            {
                ContractDefinition baseContract = context.GetASTNodeById(id) as ContractDefinition;
                VeriSolAssert(baseContract != null);

                string callee = TransUtils.GetCanonicalConstructorName(baseContract);
                if (baseContract.Name == contract.Name)
                {
                    // for current contract, call the body that does not have the base calls
                    // for base contracts, call the wrapper constructor 
                    callee += "_NoBaseCtor";
                }

                List<BoogieExpr> inputs = new List<BoogieExpr>();
                List<BoogieIdentifierExpr> outputs = new List<BoogieIdentifierExpr>();

                InheritanceSpecifier inheritanceSpecifier = GetInheritanceSpecifieWithArgsOfBase(contract, baseContract);
                if (inheritanceSpecifier != null)
                {
                    inputs.Add(new BoogieIdentifierExpr("this"));
                    inputs.Add(new BoogieIdentifierExpr("msgsender_MSG"));
                    inputs.Add(new BoogieIdentifierExpr("msgvalue_MSG"));
                    foreach (Expression argument in inheritanceSpecifier.Arguments)
                    {
                        inputs.Add(TranslateExpr(argument));
                    }
                }
                else // no argument for this base constructor
                {

                    if (baseContract.Name == contract.Name)
                    {
                        // only do this for the derived contract
                        foreach (BoogieVariable param in inParams)
                        {
                            inputs.Add(new BoogieIdentifierExpr(param.TypedIdent.Name));
                        }
                    }
                    else
                    {
                        // Do we call the constructor or assume that it is invoked in teh base contract?
                        // since it needs argument, we cannot invoke it here (Issue #101)
                        var baseCtr = context.IsConstructorDefined(baseContract) ? context.GetConstructorByContract(baseContract) : null;
                        if (baseCtr != null && baseCtr.Parameters.Length() > 0)
                        {
                            continue;
                        } else if (!currentContract.BaseContracts.Any(x => x.BaseName.Name.Equals(baseContract.Name)))
                        {
                            Console.WriteLine($"Warning!!: Invoking base constructor { callee} that is not explicitly in inheritance list of {currentContract.Name}...");
                        }
                        inputs.Add(new BoogieIdentifierExpr("this"));
                        inputs.Add(new BoogieIdentifierExpr("msgsender_MSG"));
                        inputs.Add(new BoogieIdentifierExpr("msgvalue_MSG"));
                    }
                }
                BoogieCallCmd callCmd = new BoogieCallCmd(callee, inputs, outputs);
                ctorBody.AddStatement(callCmd);
            }
            BoogieImplementation ctorImpl = new BoogieImplementation(ctorName, inParams, outParams, ctorLocalVars, ctorBody);
            context.Program.AddDeclaration(ctorImpl);
        }

        // generate actual constructor procedures that invoke constructors without base in linearized order
        private void GenerateConstructorWithBaseCalls(FunctionDefinition ctor, List<BoogieVariable> inParams)
        {
            VeriSolAssert(ctor.IsConstructor, $"{ctor.Name} is not a constructor");

            ContractDefinition contract = context.GetContractByFunction(ctor);
            string procName = contract.Name + "_" + contract.Name;

            // no output params for constructor
            List<BoogieVariable> outParams = new List<BoogieVariable>();
            List<BoogieAttribute> attributes = new List<BoogieAttribute>()
            {
                new BoogieAttribute("constructor"),
                new BoogieAttribute("public"),
            };
            if (context.TranslateFlags.GenerateInlineAttributes)
            {
                attributes.Add(new BoogieAttribute("inline", 1));
            };
            BoogieProcedure procedure = new BoogieProcedure(procName, inParams, outParams, attributes);
            context.Program.AddDeclaration(procedure);

            // skip if it in ignored set
            if (context.IsMethodInIgnoredSet(ctor, currentContract))
            {
                Console.WriteLine($"Warning!: Ignoring constructor {ctor.Name} in contract {currentContract.Name} specified using /ignoreMethod:");
            }
            else
            {
                // no local variables for constructor
                List<BoogieVariable> localVars = new List<BoogieVariable>();
                BoogieStmtList ctorBody = new BoogieStmtList();

                List<int> baseContractIds = new List<int>(contract.LinearizedBaseContracts);
                baseContractIds.Reverse();

                // Print function argument values to corral.txt for counterexample:
                PrintArguments(ctor, inParams, ctorBody);

                //Note that the current derived contract appears as a baseContractId 
                foreach (int id in baseContractIds)
                {
                    ContractDefinition baseContract = context.GetASTNodeById(id) as ContractDefinition;
                    VeriSolAssert(baseContract != null);

                    // since we are not translating any statements, currentStmtList remains null
                    currentStmtList = new BoogieStmtList();

                    string callee = TransUtils.GetCanonicalConstructorName(baseContract);
                    if (baseContract.Name == contract.Name)
                    {
                        // for current contract, call the body that does not have the base calls
                        // for base contracts, call the wrapper constructor 
                        callee += "_NoBaseCtor";
                    }
                    List<BoogieExpr> inputs = new List<BoogieExpr>();
                    List<BoogieIdentifierExpr> outputs = new List<BoogieIdentifierExpr>();

                    InheritanceSpecifier inheritanceSpecifier = GetInheritanceSpecifieWithArgsOfBase(contract, baseContract);
                    ModifierInvocation modifierInvocation = GetModifierInvocationOfBase(ctor, baseContract);
                    if (inheritanceSpecifier != null)
                    {
                        inputs.Add(new BoogieIdentifierExpr("this"));
                        inputs.Add(new BoogieIdentifierExpr("msgsender_MSG"));
                        inputs.Add(new BoogieIdentifierExpr("msgvalue_MSG"));
                        foreach (Expression argument in inheritanceSpecifier.Arguments)
                        {
                            inputs.Add(TranslateExpr(argument));
                        }
                    }
                    else if (modifierInvocation != null)
                    {
                        inputs.Add(new BoogieIdentifierExpr("this"));
                        inputs.Add(new BoogieIdentifierExpr("msgsender_MSG"));
                        inputs.Add(new BoogieIdentifierExpr("msgvalue_MSG"));
                        foreach (Expression argument in modifierInvocation.Arguments)
                        {
                            inputs.Add(TranslateExpr(argument));
                        }
                    }
                    else // no argument for this base constructor
                    {
                        if (baseContract.Name == contract.Name)
                        {
                            // only do this for the derived contract
                            foreach (BoogieVariable param in inParams)
                            {
                                inputs.Add(new BoogieIdentifierExpr(param.TypedIdent.Name));
                            }
                        }
                        else
                        {
                            // Do we call the constructor or assume that it is invoked in teh base contract?
                            // Do we call the constructor or assume that it is invoked in teh base contract?
                            // since it needs argument, we cannot invoke it here (Issue #101)
                            var baseCtr = context.IsConstructorDefined(baseContract) ? context.GetConstructorByContract(baseContract) : null;
                            if (baseCtr != null && baseCtr.Parameters.Length() > 0)
                            {
                                continue;
                            }
                            else if (!currentContract.BaseContracts.Any(x => x.BaseName.Name.Equals(baseContract.Name)))
                            {
                                Console.WriteLine($"Warning!!: Invoking base constructor { callee} that is not explicitly called in the inheritance/modifier list specified in { ctor.Name}...");
                            }
                            inputs.Add(new BoogieIdentifierExpr("this"));
                            inputs.Add(new BoogieIdentifierExpr("msgsender_MSG"));
                            inputs.Add(new BoogieIdentifierExpr("msgvalue_MSG"));
                        }
                    }
                    BoogieCallCmd callCmd = new BoogieCallCmd(callee, inputs, outputs);
                    ctorBody.AppendStmtList(currentStmtList);
                    ctorBody.AddStatement(callCmd);
                    currentStmtList = null;
                }



                localVars.AddRange(boogieToLocalVarsMap[currentBoogieProc]);
                BoogieImplementation implementation = new BoogieImplementation(procName, inParams, outParams, localVars, ctorBody);
                context.Program.AddDeclaration(implementation);
            }           
        }

        // get the inheritance specifier of `baseContract' in contract definition `contract' if it specifies arguments
        // return null if there is no matching inheritance specifier
        // NOTE: two inheritance specifiers having the same name leads to a compile error
        private InheritanceSpecifier GetInheritanceSpecifieWithArgsOfBase(ContractDefinition contract, ContractDefinition baseContract)
        {
            foreach (InheritanceSpecifier inheritanceSpecifier in contract.BaseContracts)
            {
                if (inheritanceSpecifier.Arguments != null && inheritanceSpecifier.BaseName.Name.Equals(baseContract.Name))
                {
                    return inheritanceSpecifier;
                }
            }
            return null;
        }

        // get the modifier invocation of `baseContract' in constructor `ctor'
        // return null if there is no matching modifier
        // NOTE: two constructor modifiers having the same name leads to a compile error
        private ModifierInvocation GetModifierInvocationOfBase(FunctionDefinition ctor, ContractDefinition baseContract)
        {
            foreach (ModifierInvocation modifierInvocation in ctor.Modifiers)
            {
                int id = modifierInvocation.ModifierName.ReferencedDeclaration;
                if (context.GetASTNodeById(id) is ContractDefinition contractDef)
                {
                    if (contractDef == baseContract)
                    {
                        return modifierInvocation;
                    }
                }
            }
            return null;
        }

        // updated in the visitor of parameter list
        private List<BoogieVariable> currentParamList;
        private bool isReturnParameterList = false;
        
        public override bool Visit(ParameterList node)
        {
            preTranslationAction(node);
            currentParamList = new List<BoogieVariable>();
            var retParamCount = 0;
            foreach (VariableDeclaration parameter in node.Parameters)
            {
                // Issue a warning for intXX variables in case /useModularArithemtic option is used:
                if (context.TranslateFlags.UseModularArithmetic && parameter.TypeDescriptions.IsInt())
                {
                    Console.WriteLine($"Warning: signed integer arithmetic is not handled with /useModularArithmetic option");
                }

                string name = null;
                if (String.IsNullOrEmpty(parameter.Name))
                {
                    if (isReturnParameterList)
                        name = $"__ret_{retParamCount++}_" ;
                    else
                        name = $"__param_{retParamCount++}_";
                }
                else
                {
                    name = TransUtils.GetCanonicalLocalVariableName(parameter, context);
                }
                BoogieType type = TransUtils.GetBoogieTypeFromSolidityTypeName(parameter.TypeName);
                var boogieParam = new BoogieFormalParam(new BoogieTypedIdent(name, type));
                currentParamList.Add(boogieParam);
            }
            return false;
        }

        // update in the visitor of different statements
        // this now accumulates all Boogie stmts generated when they are being generated
        private BoogieStmtList currentStmtList = null;

        /// <summary>
        /// This the only method that returns a BoogieStmtList (value of currentStmtList)
        /// </summary>
        /// <param name="node"></param>
        /// <returns></returns>
        public BoogieStmtList TranslateStatement(Statement node)
        {
            //push the current Statement
            var oldCurrentStmtList = currentStmtList; 

            //new scope
            currentStmtList = new BoogieStmtList(); // reset before starting to translate a Statement
            node.Accept(this);
            VeriSolAssert(currentStmtList != null);

            BoogieStmtList annotatedStmtList = new BoogieStmtList();
            // add source file path and line number
            if (!context.TranslateFlags.NoSourceLineInfoFlag)
            {
                BoogieAssertCmd annotationCmd = InstrumentSourceFileAndLineInfo(node);
                annotatedStmtList = BoogieStmtList.MakeSingletonStmtList(annotationCmd);
            }
            annotatedStmtList.AppendStmtList(currentStmtList);

            currentStmtList = oldCurrentStmtList; // pop the stack of currentStmtList

            return annotatedStmtList;
        }

        private BoogieAssertCmd InstrumentSourceFileAndLineInfo(ASTNode node)
        {
            var srcFileLineInfo = TransUtils.GenerateSourceInfoAnnotation(node, context);
            currentSourceFile = srcFileLineInfo.Item1;
            currentSourceLine = srcFileLineInfo.Item2;

            List<BoogieAttribute> attributes = new List<BoogieAttribute>()
                {
                new BoogieAttribute("first"),
                new BoogieAttribute("sourceFile", "\"" + srcFileLineInfo.Item1 + "\""),
                new BoogieAttribute("sourceLine", srcFileLineInfo.Item2)
                };
            BoogieAssertCmd annotationCmd = new BoogieAssertCmd(new BoogieLiteralExpr(true), attributes);
            return annotationCmd;
        }

        public override bool Visit(Block node)
        {
            preTranslationAction(node);
            BoogieStmtList block = new BoogieStmtList();
            foreach (Statement statement in node.Statements)
            {
                BoogieStmtList stmtList = TranslateStatement(statement);
                block.AppendStmtList(stmtList);
            }

            currentStmtList = block;
            return false;
        }

        public override bool Visit(PlaceholderStatement node)
        {
            preTranslationAction(node);
            return false;
        }

        public override bool Visit(VariableDeclarationStatement node)
        {
            preTranslationAction(node);
            foreach (VariableDeclaration varDecl in node.Declarations)
            {
                string name = TransUtils.GetCanonicalLocalVariableName(varDecl, context);
                BoogieType type = TransUtils.GetBoogieTypeFromSolidityTypeName(varDecl.TypeName);
                // Issue a warning for intXX variables in case /useModularArithemtic option is used:
                if (context.TranslateFlags.UseModularArithmetic && varDecl.TypeDescriptions.IsInt())
                {
                    Console.WriteLine($"Warning: signed integer arithmetic is not handled with /useModularArithmetic option");
                }
                var boogieVariable = new BoogieLocalVariable(new BoogieTypedIdent(name, type));
                boogieToLocalVarsMap[currentBoogieProc].Add(boogieVariable);
            }

            // handle the initial value of variable declaration
            if (node.InitialValue != null)
            {
                VeriSolAssert(node.Declarations.Count == 1, "Invalid multiple variable declarations");

                // de-sugar to variable declaration and an assignment
                VariableDeclaration varDecl = node.Declarations[0];

                Identifier identifier = new Identifier();
                identifier.Name = varDecl.Name;
                identifier.ReferencedDeclaration = varDecl.Id;
                identifier.TypeDescriptions = varDecl.TypeDescriptions;

                Assignment assignment = new Assignment();
                assignment.LeftHandSide = identifier;
                assignment.Operator = "=";
                assignment.RightHandSide = node.InitialValue;

                // call the visitor for assignments
                assignment.Accept(this);
            }
            else
            {
                // havoc the declared variables
                List<BoogieIdentifierExpr> varsToHavoc = new List<BoogieIdentifierExpr>();
                foreach (VariableDeclaration varDecl in node.Declarations)
                {
                    string varIdent = TransUtils.GetCanonicalLocalVariableName(varDecl, context);
                    varsToHavoc.Add(new BoogieIdentifierExpr(varIdent));
                }
                BoogieHavocCmd havocCmd = new BoogieHavocCmd(varsToHavoc);
                currentStmtList.AppendStmtList(BoogieStmtList.MakeSingletonStmtList(havocCmd));
            }
            return false;
        }

        //private BoogieExpr HelperModOper(BoogieExpr rhs, Expression lhsNode)
        //{
        //    BoogieExpr res = rhs;
        //    if (!context.TranslateFlags.UseModularArithmetic)
        //    {
        //        return res;
        //    }
        //    else if (lhsNode is TupleExpression tuple)
        //    {
        //        VeriSolAssert(false, "Not implemented... tuple in the lhs for overflow detection");
        //    }
        //    else if (lhsNode.TypeDescriptions.IsUintWSize(out uint sz))
        //    {
                
        //            Console.WriteLine("HelperModOper: UseModularArithmetic: adding mod on the rhs of asgn; lhs is uint of size {0}", sz);
        //            VeriSolAssert(sz != 0, $"size of uint lhs is zero");
        //            BigInteger maxUIntValue = (BigInteger)Math.Pow(2, sz);
        //            Console.WriteLine("HelperModOper: maxUIntValue is {0}", maxUIntValue);
        //            res = new BoogieFuncCallExpr("modBpl", new List<BoogieExpr>() { rhs, new BoogieLiteralExpr(maxUIntValue) });
        //            return res;
             
        //    }

        //    return res;
        //}

        private BoogieExpr AddModuloOp(Expression srcExpr, BoogieExpr expr, TypeDescription type)
        {
            if (context.TranslateFlags.UseModularArithmetic)
            {
                if (type != null)
                {
                    var isUint = type.IsUintWSize(srcExpr, out uint sz);
                    if (isUint)
                    {
                        VeriSolAssert(sz != 0, $"size in AddModuloOp is zero");
                        BigInteger maxUIntValue = (BigInteger)Math.Pow(2, sz);
                        return (BoogieExpr)new BoogieFuncCallExpr("modBpl", new List<BoogieExpr>() { expr, new BoogieLiteralExpr(maxUIntValue) });
                    }
                }
            }
            return expr;
        }

        public void updateAssignedSums(BoogieBinaryOperation.Opcode op, List<Expression> exprs, List<BoogieExpr> boogieExprs)
        {
            VeriSolAssert(exprs.Count == boogieExprs.Count, "Assigned expressions did not match");
            for (int i = 0; i < exprs.Count; i++)
            {
                Expression expr = exprs[i];
                var isInt = expr.TypeDescriptions.IsInt() || expr.TypeDescriptions.IsUint();
                if (isInt && expr is IndexAccess access && boogieExprs[i] is BoogieMapSelect sel && sel.BaseExpr is BoogieMapSelect arrIdent)
                {
                    currentStmtList.AddStatement(adjustSum(op,  arrIdent.Arguments[0], boogieExprs[i]));
                }
            }
        }

        public override bool Visit(Assignment node)
        {
            preTranslationAction(node);
            List<BoogieExpr> lhs = new List<BoogieExpr>();
            List<BoogieType> lhsTypes = new List<BoogieType>(); //stores types in case of tuples
            List<Expression> lhsExprs;
            
            bool isTupleAssignment = false;

            if (node.LeftHandSide is TupleExpression tuple)
            {
                // we only handle the case (e1, e2, .., _, _)  = funcCall(...)
                lhs.AddRange(tuple.Components.ConvertAll(x => TranslateExpr(x)));
                isTupleAssignment = true;
                lhsTypes.AddRange(tuple.Components.ConvertAll(x => MapArrayHelper.InferExprTypeFromTypeString(x.TypeDescriptions.TypeString)));
                lhsExprs = tuple.Components;
            }
            else
            {
                lhs.Add(TranslateExpr(node.LeftHandSide));
                lhsExprs = new List<Expression>();
                lhsExprs.Add(node.LeftHandSide);
            }

            // TODO: this part should go into Translate a function call expression
            if (node.RightHandSide is FunctionCall funcCall)
            {
                // if lhs is not an identifier (e.g. a[i]), then
                // we have to introduce a temporary
                // we do it even when lhs is identifier to keep translation simple
                var tmpVars = new List<BoogieIdentifierExpr>();

                var oldStmtList = currentStmtList;
                currentStmtList = new BoogieStmtList();

                if (!isTupleAssignment) {
                    tmpVars.Add(lhs[0] is BoogieIdentifierExpr ? lhs[0] as BoogieIdentifierExpr : MkNewLocalVariableForFunctionReturn(funcCall));
                } else {
                    // always use temporaries for tuples regardless if lhs[i] is an identifier
                    tmpVars.AddRange(lhsTypes.ConvertAll(x => MkNewLocalVariableWithType(x)));
                }

                var tmpVariableAssumes = currentStmtList;
                currentStmtList = oldStmtList;
        
                // a Boolean to decide is we needed to use tmpVar
                bool usedTmpVar = true;

                if (IsContractConstructor(funcCall))
                {
                    VeriSolAssert(!isTupleAssignment, "Not expecting a tuple for Constructors");
                    TranslateNewStatement(funcCall, tmpVars[0]);
                }
                else if (IsStructConstructor(funcCall))
                {
                    VeriSolAssert(!isTupleAssignment, "Not expecting a tuple for Constructors");
                    TranslateStructConstructor(funcCall, tmpVars[0]);
                }
                else if (IsKeccakFunc(funcCall))
                {
                    VeriSolAssert(!isTupleAssignment, "Not expecting a tuple for Keccak256");
                    TranslateKeccakFuncCall(funcCall, lhs[0]); //this is not a procedure call in Boogie
                    usedTmpVar = false;
                }
                else if (IsAbiEncodePackedFunc(funcCall))
                {
                    TranslateAbiEncodedFuncCall(funcCall, tmpVars[0]); //this is not a procedure call in Boogie
                    VeriSolAssert(!isTupleAssignment, "Not expecting a tuple for abi.encodePacked");
                    usedTmpVar = false;
                }
                else if (IsTypeCast(funcCall))
                {
                    // assume the type cast is used as: obj = C(var);
                    VeriSolAssert(!isTupleAssignment, "Not expecting a tuple for type cast");
                    bool isElementaryCast; //not used at this site
                    TranslateTypeCast(funcCall, tmpVars[0], out isElementaryCast); //this is not a procedure call in Boogie
                    usedTmpVar = false;
                }
                else // normal function calls
                {
                    VeriSolAssert(tmpVars is List<BoogieIdentifierExpr>, $"tmpVar has to be a list of Boogie identifiers: {tmpVars}");
                    TranslateFunctionCalls(funcCall, tmpVars);
                }
                if (context.TranslateFlags.InstrumentSums)
                {
                    updateAssignedSums(BoogieBinaryOperation.Opcode.SUB, lhsExprs, lhs);
                }
                if (!isTupleAssignment)
                {
                    if (usedTmpVar || !(lhs[0] is BoogieIdentifierExpr)) //bad bug: was && before!!  
                        currentStmtList.AddStatement(new BoogieAssignCmd(lhs[0], tmpVars[0]));                       
                } else
                {
                    for (int i = 0; i < lhs.Count; ++i)
                    {
                        currentStmtList.AddStatement(new BoogieAssignCmd(lhs[i], tmpVars[i]));
                    }
                }
                if (context.TranslateFlags.InstrumentSums)
                {
                    updateAssignedSums(BoogieBinaryOperation.Opcode.ADD, lhsExprs, lhs);
                }
                foreach(var block in tmpVariableAssumes.BigBlocks)
                {
                    foreach(var stmt in block.SimpleCmds)
                       currentStmtList.AddStatement(stmt);

                }
            }
            else
            {
                if (isTupleAssignment)
                    VeriSolAssert(false, "Not implemented...currently only support assignment of tuples as returns of a function call");

                BoogieExpr rhs = TranslateExpr(node.RightHandSide);
                BoogieStmtList stmtList = new BoogieStmtList();
                BoogieExpr assignedExpr = new BoogieExpr();
                switch (node.Operator)
                {
                    case "=":
                        stmtList.AddStatement(new BoogieAssignCmd(lhs[0], rhs));
                        break;
                    case "+=":
                        assignedExpr = new BoogieBinaryOperation(BoogieBinaryOperation.Opcode.ADD, lhs[0], rhs);
                        assignedExpr = AddModuloOp(node.LeftHandSide, assignedExpr, node.LeftHandSide.TypeDescriptions);
                        stmtList.AddStatement(new BoogieAssignCmd(lhs[0], assignedExpr));
                        break;
                    case "-=":
                        assignedExpr = new BoogieBinaryOperation(BoogieBinaryOperation.Opcode.SUB, lhs[0], rhs);
                        assignedExpr = AddModuloOp(node.LeftHandSide, assignedExpr, node.LeftHandSide.TypeDescriptions);
                        stmtList.AddStatement(new BoogieAssignCmd(lhs[0], assignedExpr));
                        break;
                    case "*=":
                        assignedExpr = new BoogieBinaryOperation(BoogieBinaryOperation.Opcode.MUL, lhs[0], rhs);
                        assignedExpr = AddModuloOp(node.LeftHandSide, assignedExpr, node.LeftHandSide.TypeDescriptions);
                        stmtList.AddStatement(new BoogieAssignCmd(lhs[0], assignedExpr));
                        break;
                    case "/=":
                        assignedExpr = new BoogieBinaryOperation(BoogieBinaryOperation.Opcode.DIV, lhs[0], rhs);
                        assignedExpr = AddModuloOp(node.LeftHandSide, assignedExpr, node.LeftHandSide.TypeDescriptions);
                        stmtList.AddStatement(new BoogieAssignCmd(lhs[0], assignedExpr));
                        break;
                    default:
                        VeriSolAssert(false,  $"Unknown assignment operator: {node.Operator}");
                        break;
                }
                
                if (context.TranslateFlags.InstrumentSums)
                {
                    updateAssignedSums(BoogieBinaryOperation.Opcode.SUB, lhsExprs, lhs);
                }
                currentStmtList.AppendStmtList(stmtList);
                if (context.TranslateFlags.InstrumentSums)
                {
                    updateAssignedSums(BoogieBinaryOperation.Opcode.ADD, lhsExprs, lhs);
                }
            }

            var lhsType = node.LeftHandSide.TypeDescriptions != null ?
                node.LeftHandSide.TypeDescriptions :
                (node.RightHandSide.TypeDescriptions != null ?
                    node.RightHandSide.TypeDescriptions : null);

            if (lhsType != null && !isTupleAssignment)
            {
                var callCmd = InstrumentForPrintingData(lhsType, lhs[0], node.LeftHandSide.ToString());
                if (callCmd != null)
                {
                    currentStmtList.AddStatement(callCmd);
                }
            }

            return false;
        }

        private void TranslateInBuiltFunction(FunctionCall funcCall, BoogieExpr lhs)
        {
            throw new NotImplementedException();
        }

        private void TranslateFunctionCalls(FunctionCall funcCall, List<BoogieIdentifierExpr> outParams)
        {
            if (IsExternalFunctionCall(funcCall))
            {
                TranslateExternalFunctionCall(funcCall, outParams);
            }
            else
            {
                TranslateInternalFunctionCall(funcCall, outParams);
            }
        }

        public override bool Visit(Return node)
        {
            preTranslationAction(node);
            if (node.Expression == null)
            {
                //Void
                BoogieReturnCmd returnCmd = new BoogieReturnCmd();
                if (currentPostlude == null)
                {
                    currentStmtList.AppendStmtList(BoogieStmtList.MakeSingletonStmtList(returnCmd));
                }
                else
                {
                    currentStmtList.AppendStmtList(currentPostlude);
                    currentStmtList.AddStatement(returnCmd);
                }
            }
            else
            {
                BoogieExpr retExpr = TranslateExpr(node.Expression); //TODO: handle tuples here?
                var retParamCount = 0;
                if (node.Expression is TupleExpression tuple)
                {
                    //Tuple
                    if (!(retExpr is BoogieTupleExpr))
                    {
                        VeriSolAssert(false, "Expecting a Boogie tuple expression here");
                    }
                    var bTupleExpr = retExpr as BoogieTupleExpr;

                    //turn the tuple assignment into a serial assignment [TODO: understand the evaluation semantics]
                    foreach (var retVarDecl in currentFunction.ReturnParameters.Parameters)
                    {
                        string retVarName = String.IsNullOrEmpty(retVarDecl.Name) ?
                            $"__ret_{retParamCount}_" :
                            TransUtils.GetCanonicalLocalVariableName(retVarDecl, context);
                        BoogieIdentifierExpr retVar = new BoogieIdentifierExpr(retVarName);
                        BoogieAssignCmd assignCmd = new BoogieAssignCmd(retVar, bTupleExpr.Arguments[retParamCount++]);
                        currentStmtList.AppendStmtList(BoogieStmtList.MakeSingletonStmtList(assignCmd)); //TODO: simultaneous updates
                    }
                }
                else
                {
                    //Singleton 
                    var retVarDecl = currentFunction.ReturnParameters.Parameters[0];
                    string retVarName = String.IsNullOrEmpty(retVarDecl.Name) ?
                        $"__ret_{retParamCount++}_" :
                        TransUtils.GetCanonicalLocalVariableName(retVarDecl, context);
                    BoogieIdentifierExpr retVar = new BoogieIdentifierExpr(retVarName);
                    BoogieAssignCmd assignCmd = new BoogieAssignCmd(retVar, retExpr);
                    currentStmtList.AppendStmtList(BoogieStmtList.MakeSingletonStmtList(assignCmd)); //TODO: simultaneous updates
                }

                if (currentPostlude != null)
                {
                    currentStmtList.AppendStmtList(currentPostlude);
                }
                // add a return command, in case the original return expr is in the middle of the function body
                currentStmtList.AddStatement(new BoogieReturnCmd());
            }
            return false;
        }

        private void AddAssumeForUints(Expression expr, BoogieExpr boogieExpr, TypeDescription typeDesc)
        {
            // skip based on a flag
            if (context.TranslateFlags.NoUnsignedAssumesFlag)
                return;

            // Add positive number assume for uints
            if (typeDesc!=null && typeDesc.IsUint())
            {
                var ge0 = new BoogieBinaryOperation(BoogieBinaryOperation.Opcode.GE, boogieExpr, new BoogieLiteralExpr(BigInteger.Zero));

                if (context.TranslateFlags.UseModularArithmetic)
                {
                    var isUint = typeDesc.IsUintWSize(expr, out uint sz);
                    if (isUint)
                    {
                        VeriSolAssert(sz != 0, $"size of uint lhs is zero");
                        BigInteger maxUIntValue = (BigInteger)Math.Pow(2, sz);
                        var tmp = new BoogieBinaryOperation(BoogieBinaryOperation.Opcode.LT, boogieExpr, new BoogieLiteralExpr(maxUIntValue));
                        ge0 = new BoogieBinaryOperation(BoogieBinaryOperation.Opcode.AND, ge0, tmp);
                    }
                }


                var assumePositiveCmd = new BoogieAssumeCmd(ge0);
                currentStmtList.AppendStmtList(BoogieStmtList.MakeSingletonStmtList(assumePositiveCmd));
            }
        }
        
        private void emitRevertLogic(BoogieStmtList revertLogic)
        {
            BoogieAssignCmd setRevert = new BoogieAssignCmd(new BoogieIdentifierExpr("revert"), new BoogieLiteralExpr(true));
            revertLogic.AddStatement(setRevert);

            revertLogic.AddStatement(new BoogieReturnCmd());
        }

        public override bool Visit(Throw node)
        {
            preTranslationAction(node);
            if (!context.TranslateFlags.ModelReverts)
            {
                BoogieAssumeCmd assumeCmd = new BoogieAssumeCmd(new BoogieLiteralExpr(false));
                currentStmtList.AppendStmtList(BoogieStmtList.MakeSingletonStmtList(assumeCmd));
            }
            else
            {
                emitRevertLogic(currentStmtList);
            }

            return false;
        }

        public override bool Visit(IfStatement node)
        {
            preTranslationAction(node);
            BoogieExpr guard = TranslateExpr(node.Condition);
            BoogieStmtList thenBody = TranslateStatement(node.TrueBody);
            BoogieStmtList elseBody = null;
            if (node.FalseBody != null)
            {
                elseBody = TranslateStatement(node.FalseBody);
            }
            BoogieIfCmd ifCmd = new BoogieIfCmd(guard, thenBody, elseBody);

            //currentStmtList = new BoogieStmtList();
            //currentStmtList.AppendStmtList(auxStmtList);
            currentStmtList.AddStatement(ifCmd);
            return false;
        }

        public override bool Visit(WhileStatement node)
        {
            preTranslationAction(node);
            BoogieExpr guard = TranslateExpr(node.Condition);
            BoogieStmtList body = TranslateStatement(node.Body);

            BoogieStmtList newBody;
            var invariants = ExtractSpecifications("Invariant_VeriSol", body, out newBody);
            BoogieWhileCmd whileCmd = new BoogieWhileCmd(guard, newBody, invariants);

            currentStmtList.AddStatement(whileCmd);

            if (context.TranslateFlags.InstrumentGas &&
                context.TranslateFlags.ModelReverts)
            {
                emitGasCheck(newBody);
            }
            return false;
        }

        public override bool Visit(ForStatement node)
        {
            preTranslationAction(node);
            BoogieStmtList initStmt = TranslateStatement(node.InitializationExpression);
            BoogieExpr guard = TranslateExpr(node.Condition);
            BoogieStmtList loopStmt = TranslateStatement(node.LoopExpression);
            BoogieStmtList body = TranslateStatement(node.Body);

            BoogieStmtList stmtList = new BoogieStmtList();
            stmtList.AppendStmtList(initStmt);

            body.AppendStmtList(loopStmt);

            BoogieStmtList newBody;
            var invariants = ExtractSpecifications("Invariant_VeriSol", body, out newBody);
            BoogieWhileCmd whileCmd = new BoogieWhileCmd(guard, newBody, invariants);
            stmtList.AddStatement(whileCmd);

            currentStmtList.AppendStmtList(stmtList);

            if (context.TranslateFlags.InstrumentGas &&
                context.TranslateFlags.ModelReverts)
            {
                emitGasCheck(newBody);
            }
            return false;
        }


        /// <summary>
        /// 
        /// </summary>
        /// <param name="specStringCall">"Invariant_VeriSol", "Ensures_VeriSol", "Requires_VeriSol"</param>
        /// <param name="body"></param>
        /// <param name="bodyWithoutSpecNodes"></param>
        /// <returns></returns>
        private List<BoogieExpr> ExtractSpecifications(string specStringCall, BoogieStmtList body, out BoogieStmtList bodyWithoutSpecNodes)
        {
            bodyWithoutSpecNodes = new BoogieStmtList();
            List<BoogieExpr> specArguments = new List<BoogieExpr>();
            foreach (var bigBlock in body.BigBlocks)
            {
                foreach (var stmt in bigBlock.SimpleCmds)
                {
                    var callCmd = stmt as BoogieCallCmd;
                    if (callCmd == null)
                    {
                        bodyWithoutSpecNodes.AddStatement(stmt);
                        continue;
                    }
                    if (callCmd.Callee.Equals(specStringCall))
                    {
                        //first 3 args are {this, msg.sender, msg.value}
                        if (specStringCall.Equals("Modifies_VeriSol"))
                        {
                            VeriSolAssert(callCmd.Ins.Count == 5, $"For Modifies clause, found {specStringCall}(..) with unexpected number of args (expected 5)");
                            specArguments.Add(TranslateModifiesStmt(callCmd.Ins[3], callCmd.Ins[4]));
                        }
                        else
                        {
                            VeriSolAssert(callCmd.Ins.Count == 4, $"Found {specStringCall}(..) with unexpected number of args (expected 4)");
                            specArguments.Add(callCmd.Ins[3]);
                        }
                    } else
                    {
                        bodyWithoutSpecNodes.AddStatement(stmt);
                    }
                }
            }
            return specArguments;
        }

        private BoogieExpr TranslateModifiesStmt(BoogieExpr boogieExpr1, BoogieExpr boogieExpr2)
        {
            //has to be M_ref_int[mapp[this]] instead of mapp[this]
            var mapName = MapArrayHelper.GetMemoryMapName(BoogieType.Ref, BoogieType.Int);
            var mappingExpr = new BoogieMapSelect(new BoogieIdentifierExpr(mapName), boogieExpr1);

            //boogieExpr2 is a tuple, we need to flatten it into an array
            var boogieTupleExpr = boogieExpr2 as BoogieTupleExpr;
            VeriSolAssert(boogieTupleExpr != null, $"Expecting tuple expression, found {boogieExpr2.ToString()}");
            VeriSolAssert(boogieTupleExpr.Arguments.Count > 0, $"Expecting non-tuple expression, found {boogieExpr2.ToString()}");
            VeriSolAssert(boogieTupleExpr.Arguments.Count < 10, $"Expecting tuple expression of size < 10, found {boogieExpr2.ToString()}");

            // forall x: Ref :: x == m1 || x == m2 ... || x == mi || map[x] == old(map[x])
            var qVar1 = QVarGenerator.NewQVar(0, 0);
            var bodyExpr = (BoogieExpr)new BoogieLiteralExpr(false);
            for (int i = 0; i < boogieTupleExpr.Arguments.Count; i++)
            {
                bodyExpr =
                    new BoogieBinaryOperation(BoogieBinaryOperation.Opcode.OR, bodyExpr,
                       (new BoogieBinaryOperation(BoogieBinaryOperation.Opcode.EQ, qVar1, boogieTupleExpr.Arguments[i])));
            }
            var mapExpr = new BoogieMapSelect(mappingExpr, (BoogieExpr)qVar1);
            var oldMapExpr = new BoogieFuncCallExpr("old", new List<BoogieExpr>() { mapExpr });
            var eqExpr = new BoogieBinaryOperation(BoogieBinaryOperation.Opcode.EQ, mapExpr, oldMapExpr);
            bodyExpr = new BoogieBinaryOperation(BoogieBinaryOperation.Opcode.OR, bodyExpr, eqExpr);
            var qBodyExpr = new BoogieQuantifiedExpr(true,
                new List<BoogieIdentifierExpr>() { qVar1 },
                new List<BoogieType>() { BoogieType.Ref },
                bodyExpr
                );
            return qBodyExpr;
        }

        private List<BoogieExpr> ExtractContractInvariants(BoogieStmtList body)
        {
            List<BoogieExpr> invariantExprs = new List<BoogieExpr>();
            foreach (var bigBlock in body.BigBlocks)
            {
                foreach (var stmt in bigBlock.SimpleCmds)
                {
                    var callCmd = stmt as BoogieCallCmd;
                    if (callCmd == null)
                    {
                        continue;
                    }
                    if (callCmd.Callee.Equals("ContractInvariant_VeriSol"))
                    {
                        VeriSolAssert(callCmd.Ins.Count == 4, "Found VeriSol.ContractInvariant(..) with unexpected number of args");
                        invariantExprs.Add(callCmd.Ins[3]);
                    }
                }
            }
            return invariantExprs;
        }

        public override bool Visit(DoWhileStatement node)
        {
            preTranslationAction(node);
            BoogieExpr guard = TranslateExpr(node.Condition);
            BoogieStmtList body = TranslateStatement(node.Body);

            BoogieStmtList stmtList = new BoogieStmtList();

            BoogieStmtList newBody;
            var invariants = ExtractSpecifications("Invariant_VeriSol", body, out newBody);
            stmtList.AppendStmtList(newBody);

            BoogieWhileCmd whileCmd = new BoogieWhileCmd(guard, newBody, invariants);

            stmtList.AddStatement(whileCmd);

            currentStmtList.AppendStmtList(stmtList);
            
            if (context.TranslateFlags.InstrumentGas &&
                context.TranslateFlags.ModelReverts)
            {
                emitGasCheck(newBody);
            }
            return false;
        }

        public override bool Visit(Break node)
        {
            preTranslationAction(node);
            BoogieBreakCmd breakCmd = new BoogieBreakCmd();
            currentStmtList.AddStatement(breakCmd);
            return false;
        }

        public override bool Visit(Continue node)
        {
            preTranslationAction(node);
            throw new NotImplementedException(node.ToString());
        }

        public override bool Visit(ExpressionStatement node)
        {
            preTranslationAction(node);
            if (node.Expression is UnaryOperation unaryOperation)
            {
                // only handle increment and decrement operators in a separate statement
                VeriSolAssert(!(unaryOperation.SubExpression is UnaryOperation));
                
                BoogieExpr lhs = TranslateExpr(unaryOperation.SubExpression);
                if (unaryOperation.Operator.Equals("++") ||
                    unaryOperation.Operator.Equals("--"))
                {
                    var oper = unaryOperation.Operator.Equals("++") ? BoogieBinaryOperation.Opcode.ADD : BoogieBinaryOperation.Opcode.SUB;
                    BoogieExpr rhs = new BoogieBinaryOperation(oper, lhs, new BoogieLiteralExpr(1));
                    rhs = AddModuloOp(unaryOperation.SubExpression, rhs, unaryOperation.SubExpression.TypeDescriptions);
                    BoogieAssignCmd assignCmd = new BoogieAssignCmd(lhs, rhs);
                    currentStmtList.AddStatement(assignCmd);
                    
                    var typeDescription = unaryOperation.SubExpression.TypeDescriptions;
                    var isInt = typeDescription.IsInt() || typeDescription.IsUint();
                    if (context.TranslateFlags.InstrumentSums && isInt && unaryOperation.SubExpression is IndexAccess access &&
                        lhs is BoogieMapSelect sel && sel.BaseExpr is BoogieMapSelect arrIdent)
                    {
                        currentStmtList.AddStatement(adjustSum(oper,  arrIdent.Arguments[0], new BoogieLiteralExpr(BigInteger.One)));
                    }
                    
                    //print the value
                    if (!context.TranslateFlags.NoDataValuesInfoFlag)
                    {
                        var callCmd = new BoogieCallCmd("boogie_si_record_sol2Bpl_int", new List<BoogieExpr>() { lhs }, new List<BoogieIdentifierExpr>());
                        callCmd.Attributes = new List<BoogieAttribute>();
                        callCmd.Attributes.Add(new BoogieAttribute("cexpr", $"\"{unaryOperation.SubExpression.ToString()}\""));
                        currentStmtList.AddStatement(callCmd);
                    }
                    AddAssumeForUints(unaryOperation, lhs, unaryOperation.TypeDescriptions);
                }
                else if (unaryOperation.Operator.Equals("delete"))
                {
                    var typeDescription = unaryOperation.SubExpression.TypeDescriptions;
                    var isBasicType = typeDescription.IsInt() || typeDescription.IsUint()
                                      || typeDescription.IsBool() || typeDescription.IsString();

                    // var isArrayAccess = unaryOperation.SubExpression is IndexAccess;
                    if (typeDescription.IsDynamicArray())
                    {
                        BoogieExpr element = TranslateExpr(unaryOperation.SubExpression);
                        BoogieExpr lengthMapSelect = new BoogieMapSelect(new BoogieIdentifierExpr("Length"), element);
                        BoogieExpr rhs = new BoogieLiteralExpr(BigInteger.Zero);
                        var assignCmd = new BoogieAssignCmd(lengthMapSelect, rhs);
                        currentStmtList.AddStatement(assignCmd);

                        var isInt = typeDescription.IsInt() || typeDescription.IsUint();
                        if (context.TranslateFlags.InstrumentSums && isInt)
                        {
                            BoogieExpr sumExpr = getSumAccess(element);
                            var sumAssign = new BoogieAssignCmd(sumExpr, new BoogieLiteralExpr(BigInteger.Zero));
                            currentStmtList.AddStatement(sumAssign);
                        }
                    }
                    else if (typeDescription.IsStaticArray())
                    {
                        // TODO: Handle static arrauy
                        Console.WriteLine($"Warning!!: Currently not handling delete of static arrays");
                    }
                    // This handle cases like delete x with "x" a basic type or delete x[i] when x[i] being a basic type;
                    else if (isBasicType)
                    {
                        var isInt = typeDescription.IsInt() || typeDescription.IsUint();
                        if (context.TranslateFlags.InstrumentSums && isInt && unaryOperation.SubExpression is IndexAccess access &&
                            lhs is BoogieMapSelect sel && sel.BaseExpr is BoogieMapSelect arrIdent)
                        {
                            currentStmtList.AddStatement(adjustSum(BoogieBinaryOperation.Opcode.SUB, arrIdent.Arguments[0], lhs));
                        }
                        
                        BoogieExpr rhs = null;
                        if (typeDescription.IsInt() || typeDescription.IsUint())
                            rhs = new BoogieLiteralExpr(BigInteger.Zero);
                        else if (typeDescription.IsBool())
                            rhs = new BoogieLiteralExpr(false);
                        else if (typeDescription.IsString())
                        {
                            var emptyStr = "";
                            rhs = new BoogieLiteralExpr(new BigInteger(emptyStr.GetHashCode()));
                        }
                        var assignCmd = new BoogieAssignCmd(lhs, rhs);
                        currentStmtList.AddStatement(assignCmd);
                    }
                    else
                    {
                        Console.WriteLine($"Warning!!: Only handle delete for scalars and arrays, found {typeDescription.TypeString}");
                    }
                }
                return false;
            }
            else
            {
                // distribute to different visit functions
                return true;
            }
        }

        // updated in visitors of different expressions
        private BoogieExpr currentExpr;

        public Dictionary<string, List<BoogieExpr>> ContractInvariants { get => contractInvariants;}

        private BoogieExpr TranslateExpr(Expression expr)
        {
            currentExpr = null;
            if (expr is FunctionCall && IsTypeCast((FunctionCall) expr))
            {
                expr.Accept(this);
                VeriSolAssert(currentExpr != null);
            }
            else if(expr is TupleExpression tuple)
            {
                var transArgs = new List<BoogieExpr>();
                foreach(var e in tuple.Components)
                {
                    e.Accept(this);
                    VeriSolAssert(currentExpr != null);
                    transArgs.Add(currentExpr);
                }
                currentExpr = new BoogieTupleExpr(transArgs);
            }
            else
            {
                expr.Accept(this);
                VeriSolAssert(currentExpr != null);
            }

            // TODO: Many times the type is unknown...
            if(expr.TypeDescriptions!=null) //  && currentExpr is BoogieIdentifierExpr)
            {
                AddAssumeForUints(expr, currentExpr, expr.TypeDescriptions);
            }

            return currentExpr;
        }

        public override bool Visit(Literal node)
        {
            preTranslationAction(node);
            if (node.Kind.Equals("bool"))
            {
                bool b = Convert.ToBoolean(node.Value);
                currentExpr = new BoogieLiteralExpr(b);
            }
            else if (node.Kind.Equals("number"))
            {
                currentExpr = TranslateNumberToExpr(node);
            }
            else if (node.Kind.Equals("string"))
            {
                int hashCode = node.Value.GetHashCode();
                BigInteger num = new BigInteger(hashCode);
                currentExpr = new BoogieLiteralExpr(num);
            }
            else
            {
               VeriSolAssert(false, $"Unknown literal kind: {node.Kind}");
            }
            return false;
        }

        private BoogieExpr TranslateNumberToExpr(Literal node)
        {
            // now any 0x and 000 is treated as uint

            BigInteger num; 
            if (node.Value.StartsWith("0x") || node.Value.StartsWith("0X"))
            {
                num = BigInteger.Parse(node.Value.Substring(2), NumberStyles.AllowHexSpecifier);
            }
            else
            {
                num = BigInteger.Parse(node.Value);
            }

            //if (node.TypeDescriptions.IsAddress())
            //{
            //    if (num == BigInteger.Zero)
            //    {
            //        return new BoogieIdentifierExpr("null");
            //    }
            //    else
            //    {
            //        return new BoogieFuncCallExpr("ConstantToRef", new List<BoogieExpr>() { new BoogieLiteralExpr(num) });
            //    }
            //} else
            {
                return new BoogieLiteralExpr(num);
            }
        }

        public override bool Visit(Identifier node)
        {
            preTranslationAction(node);
            if (node.Name.Equals("this"))
            {
                currentExpr = new BoogieIdentifierExpr("this");
            }
            else if (node.Name.Equals("now"))
            {
                currentExpr = new BoogieIdentifierExpr("now");
            }
            else // explicitly defined identifiers
            {
                VeriSolAssert(context.HasASTNodeId(node.ReferencedDeclaration), $"Unknown node: {node}");
                VariableDeclaration varDecl = context.GetASTNodeById(node.ReferencedDeclaration) as VariableDeclaration;
                VeriSolAssert(varDecl != null);

                if (varDecl.StateVariable)
                {
                    string name = TransUtils.GetCanonicalStateVariableName(varDecl, context);

                    BoogieIdentifierExpr mapIdentifier = new BoogieIdentifierExpr(name);
                    BoogieMapSelect mapSelect = new BoogieMapSelect(mapIdentifier, new BoogieIdentifierExpr("this"));
                    currentExpr = mapSelect;
                }
                else
                {
                    string name = TransUtils.GetCanonicalLocalVariableName(varDecl, context);
                    BoogieIdentifierExpr identifier = new BoogieIdentifierExpr(name);
                    currentExpr = identifier;
                }
            }
            return false;
        }

        public override bool Visit(MemberAccess node)
        {
            preTranslationAction(node);
            // length attribute of arrays
            if (node.MemberName.Equals("length"))
            {
                currentExpr = TranslateArrayLength(node);
                return false;
            }

            if (node.MemberName.Equals("balance"))
            {
                currentExpr = TranslateBalance(node);
                return false;
            }

            // only structs will need to use x.f.g notation, since 
            // one can only access functions of nested contracts
            // RESTRICTION: only handle e.f where e is Identifier | IndexExpr | FunctionCall
            VeriSolAssert(node.Expression is Identifier || node.Expression is IndexAccess || node.Expression is FunctionCall,
                $"Only handle non-nested structures, found {node.Expression.ToString()}");
            if (node.Expression.TypeDescriptions.IsStruct())
            {
                var baseExpr = TranslateExpr(node.Expression);
                var memberMap = node.MemberName + "_" + node.Expression.TypeDescriptions.TypeString.Split(" ")[1];
                currentExpr = new BoogieMapSelect(
                    new BoogieIdentifierExpr(memberMap),
                    baseExpr);
                return false;
            }


            if (node.Expression is Identifier)
            {
                Identifier identifier = node.Expression as Identifier;
                if (identifier.Name.Equals("msg"))
                {
                    if (node.MemberName.Equals("sender"))
                    {
                        currentExpr = new BoogieIdentifierExpr("msgsender_MSG");
                    }
                    else if (node.MemberName.Equals("value"))
                    {
                        currentExpr = new BoogieIdentifierExpr("msgvalue_MSG");
                    }
                    else
                    {
                        VeriSolAssert(false, $"Unknown member for msg: {node}");
                    }
                    return false;
                }
                else if (identifier.Name.Equals("this"))
                {
                    if (node.MemberName.Equals("balance"))
                    {
                        currentExpr = new BoogieMapSelect(new BoogieIdentifierExpr("balance_ADDR"), new BoogieIdentifierExpr("this"));
                    }
                    return false;
                } 
                else if (identifier.Name.Equals("block"))
                {
                    if (node.MemberName.Equals("timestamp"))
                        currentExpr = new BoogieIdentifierExpr("now");
                    else
                        //we will havoc the value
                        currentExpr = GenerateNonDetExpr(node, node.ToString());
                    return false;
                }
                else if(identifier.Name.Equals("tx"))
                {
                    currentExpr = GenerateNonDetExpr(node, node.ToString());
                    return false;
                }

                VeriSolAssert(context.HasASTNodeId(identifier.ReferencedDeclaration), $"Unknown node: {identifier}");
                ASTNode refDecl = context.GetASTNodeById(identifier.ReferencedDeclaration);

                if (refDecl is EnumDefinition)
                {
                    int enumIndex = TransUtils.GetEnumValueIndex((EnumDefinition)refDecl, node.MemberName);
                    currentExpr = new BoogieLiteralExpr(enumIndex);
                    return false;
                }
                else
                {
                    return true;
                }
            }
            else
            {
                VeriSolAssert(false, $"Unknown expression type for member access: {node}");
                throw new Exception();
            }
        }

        private BoogieExpr GenerateNonDetExpr(Expression node, string reason)
        {
            BoogieType bType = null;
            if (node.TypeDescriptions.IsInt() || node.TypeDescriptions.IsUint())
            {
                bType = BoogieType.Int;
            }
            else if (node.TypeDescriptions.IsAddress())
            {
                bType = BoogieType.Ref;
            }
            else
            {
                VeriSolAssert(false, $"Unhandled expression {node.ToString()} with return type not equal to uint/address");
            }
            var tmpVar = MkNewLocalVariableWithType(bType);
            currentStmtList.AddStatement(new BoogieCommentCmd($"Non-deterministic value to model {reason}"));
            currentStmtList.AddStatement(new BoogieHavocCmd(new BoogieIdentifierExpr(tmpVar.Name)));
            return tmpVar;
        }

        private BoogieExpr TranslateBalance(MemberAccess node)
        {
            BoogieExpr indexExpr = TranslateExpr(node.Expression);
            var mapSelect = new BoogieMapSelect(new BoogieIdentifierExpr("Balance"), indexExpr);
            return mapSelect;
        }

        private BoogieExpr TranslateArrayLength(MemberAccess node)
        {
            VeriSolAssert(node.MemberName.Equals("length"));

            BoogieExpr indexExpr = TranslateExpr(node.Expression);
            var mapSelect = new BoogieMapSelect(new BoogieIdentifierExpr("Length"), indexExpr);
            return mapSelect;
        }

        public override bool Visit(FunctionCall node)
        {
            preTranslationAction(node);

            // VeriSolAssert(!(node.Expression is NewExpression), $"new expressions should be handled in assignment");
            if (node.Expression is NewExpression)
            {
                BoogieIdentifierExpr tmpVarExpr = MkNewLocalVariableForFunctionReturn(node);
                TranslateNewStatement(node, tmpVarExpr);
                currentExpr = tmpVarExpr;
                return false;
            }

            var functionName = TransUtils.GetFuncNameFromFuncCall(node);

            if (functionName.Equals("assert"))
            {
                // TODO:
                //countNestedFuncCallsRelExprs--;
                VeriSolAssert(node.Arguments.Count == 1);
                BoogieExpr predicate = TranslateExpr(node.Arguments[0]);
                BoogieAssertCmd assertCmd = new BoogieAssertCmd(predicate);
                currentStmtList.AddStatement(assertCmd);
            }
            else if (functionName.Equals("require"))
            {
                // TODO:
                //countNestedFuncCallsRelExprs--;
                VeriSolAssert(node.Arguments.Count == 1 || node.Arguments.Count == 2);
                BoogieExpr predicate = TranslateExpr(node.Arguments[0]);

                if (!context.TranslateFlags.ModelReverts)
                {
                    BoogieAssumeCmd assumeCmd = new BoogieAssumeCmd(predicate);
                    currentStmtList.AddStatement(assumeCmd);
                }
                else
                {
                    BoogieStmtList revertLogic = new BoogieStmtList();

                    emitRevertLogic(revertLogic);

                    BoogieIfCmd requierCheck = new BoogieIfCmd(new BoogieUnaryOperation(BoogieUnaryOperation.Opcode.NOT, predicate), revertLogic, null);

                    currentStmtList.AddStatement(requierCheck);
                }
            }
            else if (functionName.Equals("revert"))
            {
                VeriSolAssert(node.Arguments.Count == 0 || node.Arguments.Count == 1);
                if (!context.TranslateFlags.ModelReverts)
                {
                    BoogieAssumeCmd assumeCmd = new BoogieAssumeCmd(new BoogieLiteralExpr(false));
                    currentStmtList.AddStatement(assumeCmd);
                }
                else
                {
                    emitRevertLogic(currentStmtList);
                }
            }
            else if (IsImplicitFunc(node))
            {
                BoogieIdentifierExpr tmpVarExpr = MkNewLocalVariableForFunctionReturn(node);
                bool isElementaryCast = false;  

                if (IsContractConstructor(node)) {
                    TranslateNewStatement(node, tmpVarExpr);
                } else if (IsTypeCast(node)) {
                    TranslateTypeCast(node, tmpVarExpr, out isElementaryCast);
                } else if (IsAbiEncodePackedFunc(node)) {
                    TranslateAbiEncodedFuncCall(node, tmpVarExpr);
                } else if (IsKeccakFunc(node)) {
                    TranslateKeccakFuncCall(node, tmpVarExpr);
                } else if (IsStructConstructor(node)) {
                    TranslateStructConstructor(node, tmpVarExpr);
                } else
                {
                    VeriSolAssert(false, $"Unexpected implicit function {node.ToString()}");
                }

                if (!isElementaryCast)
                {
                    // We should not introduce temporaries for address(this).balance in a specification
                    currentExpr = tmpVarExpr;
                }
            }
            else if (IsVeriSolCodeContractFunction(node))
            {
                // we cannot use temporaries as we are translating a specification
                currentExpr = TranslateVeriSolCodeContractFuncCall(node);
            }
            else if (context.HasEventNameInContract(currentContract, functionName))
            {
                // generate empty statement list to ignore the event call                
                List<BoogieAttribute> attributes = new List<BoogieAttribute>()
                {
                new BoogieAttribute("EventEmitted", "\"" + functionName + "_" + currentContract.Name + "\""),
                };
                currentStmtList.AddStatement(new BoogieAssertCmd(new BoogieLiteralExpr(true), attributes));
            }
            else if (functionName.Equals("call"))
            {
                TranslateCallStatement(node);
            }
            else if (functionName.Equals("delegatecall"))
            {
                VeriSolAssert(false, "low-level delegatecall statements not supported...");
            }
            else if (IsBuiltInTransferFunc(functionName, node))
            {
                TranslateTransferCallStmt(node);
            }
            else if (functionName.Equals("send"))
            {
                var tmpVarExpr = MkNewLocalVariableForFunctionReturn(node);
                var amountExpr = TranslateExpr(node.Arguments[0]);
                TranslateSendCallStmt(node, tmpVarExpr, amountExpr);
                currentExpr = tmpVarExpr;
            }
            else if (IsDynamicArrayPush(node))
            {
                TranslateDynamicArrayPush(node);
            }
            else if (IsExternalFunctionCall(node))
            {
                // external function calls

                var memberAccess = node.Expression as MemberAccess;
                VeriSolAssert(memberAccess != null, $"An external function has to be a member access, found {node.ToString()}");

                // HACK: this is the way to identify the return type is void and hence don't need temporary variable
                if (node.TypeDescriptions.TypeString != "tuple()")
                {
                    var tmpVarExpr = MkNewLocalVariableForFunctionReturn(node);
                    var outParams = new List<BoogieIdentifierExpr>() { tmpVarExpr };
                    TranslateExternalFunctionCall(node, outParams);
                    currentExpr = tmpVarExpr;
                }
                else
                {
                    TranslateExternalFunctionCall(node);
                }
            }
            else // internal function calls
            {
                // HACK: this is the way to identify the return type is void and hence don't need temporary variable
                if (node.TypeDescriptions.TypeString != "tuple()")
                {
                    // internal function calls
                    var tmpVarExpr = MkNewLocalVariableForFunctionReturn(node);
                    var outParams = new List<BoogieIdentifierExpr>() { tmpVarExpr };
                    TranslateInternalFunctionCall(node, outParams);
                    currentExpr = tmpVarExpr;
                }
                else
                {
                    TranslateInternalFunctionCall(node);
                }

            }
            return false;
        }

        private bool IsBuiltInTransferFunc(string functionName, FunctionCall node)
        {
            if (!functionName.Equals("transfer")) return false;
            if (node.Expression is MemberAccess member)
            {
                if (member.Expression.TypeDescriptions.IsAddress())
                    return true;
            }
            return false;
        }

        private BoogieExpr TranslateVeriSolCodeContractFuncCall(FunctionCall node)
        {
            var verisolFunc = GetVeriSolCodeContractFunction(node);
            VeriSolAssert(verisolFunc != null, $"Unknown VeriSol code contracts function {node.ToString()}");
            var boogieExprs = node.Arguments.ConvertAll(x => TranslateExpr(x));
            // HACK for Sum
            if (verisolFunc.Equals("_SumMapping_VeriSol"))
            {
                //has to be M_ref_int[mapp[this]] instead of mapp[this]
                var mapName = MapArrayHelper.GetMemoryMapName(BoogieType.Ref, BoogieType.Int);
                boogieExprs[0] = new BoogieMapSelect(new BoogieIdentifierExpr(mapName), boogieExprs[0]);
            }
            return new BoogieFuncCallExpr(verisolFunc, boogieExprs);
            }

        private bool IsVeriSolCodeContractFunction(FunctionCall node)
        {
            if (node.Expression is MemberAccess member)
            {
                if (member.Expression is Identifier ident)
                {
                    if (ident.Name.Equals("VeriSol"))
                    {
                        // ignore the specifiction functions
                        if (member.MemberName.Equals("Invariant") ||
                            member.MemberName.Equals("ContractInvariant") ||
                            member.MemberName.Equals("Requires") ||
                            member.MemberName.Equals("Ensures") ||
                            member.MemberName.Equals("Modifies"))
                            return false;
                        else
                            return true;
                    }
                }
            }
            return false;
        }

        private string GetVeriSolCodeContractFunction(FunctionCall node)
        {
            if (node.Expression is MemberAccess member)
            {
                if (member.Expression is Identifier ident)
                {
                    if (ident.Name.Equals("VeriSol"))
                    {
                        if (member.MemberName.Equals("SumMapping"))
                            return "_SumMapping_VeriSol";
                        if (member.MemberName.Equals("Old"))
                            return "old"; //map it old(..) in Boogie
                        if (member.MemberName.Equals("Modifies"))
                            return "Modifies"; //map it modifies and forall(..) in Boogie
                        else
                            return null;
                    }
                }
            }
            return null;
        }

        private BoogieIdentifierExpr MkNewLocalVariableForFunctionReturn(FunctionCall node)
        {
            var boogieTypeCall = MapArrayHelper.InferExprTypeFromTypeString(node.TypeDescriptions.TypeString);


            var newBoogieVar =  MkNewLocalVariableWithType(boogieTypeCall);

            Debug.Assert(currentStmtList != null);
            AddAssumeForUints(node, newBoogieVar, node.TypeDescriptions);

            return newBoogieVar;
        }

        private BoogieIdentifierExpr MkNewLocalVariableWithType(BoogieType boogieTypeCall)
        {
            var tmpVar = new BoogieLocalVariable(context.MakeFreshTypedIdent(boogieTypeCall));
            boogieToLocalVarsMap[currentBoogieProc].Add(tmpVar);

            var tmpVarExpr = new BoogieIdentifierExpr(tmpVar.Name);

            return tmpVarExpr;
        }

        #region implicit functions 
        /// <summary>
        /// Implicit function calls
        /// </summary>
        /// <param name="node"></param>
        /// <returns></returns>
        private bool IsImplicitFunc(FunctionCall node)
        {
            return
                IsKeccakFunc(node) ||
                IsAbiEncodePackedFunc(node) ||
                IsTypeCast(node) ||
                IsStructConstructor(node) ||
                IsContractConstructor(node);
         }

        private bool IsContractConstructor(FunctionCall node)
        {
            return node.Expression is NewExpression;
        }

        private bool IsStructConstructor(FunctionCall node)
        {
            return node.Kind.Equals("structConstructorCall");
        }

        private bool IsKeccakFunc(FunctionCall node)
        {
            if (node.Expression is Identifier ident)
            {
                return ident.Name.Equals("keccak256");
            }
            return false;
        }

        private bool IsAbiEncodePackedFunc(FunctionCall node)
        {
            if (node.Expression is MemberAccess member)
            {
                if (member.Expression is Identifier ident)
                {
                    if (ident.Name.Equals("abi"))
                    {
                        if (member.MemberName.Equals("encodePacked"))
                            return true;
                    }
                }
            }
            return false;
        }

        private void TranslateKeccakFuncCall(FunctionCall funcCall, BoogieExpr lhs)
        {
            var expression = funcCall.Arguments[0];
            var boogieExpr = TranslateExpr(expression);
            var keccakExpr = new BoogieFuncCallExpr("keccak256", new List<BoogieExpr>() { boogieExpr });
            currentStmtList.AddStatement(new BoogieAssignCmd(lhs, keccakExpr));
            BoogieExpr nonZeroHashExpr = new BoogieBinaryOperation(BoogieBinaryOperation.Opcode.NEQ, lhs, new BoogieLiteralExpr(BigInteger.Zero));
            currentStmtList.AddStatement(new BoogieAssumeCmd(nonZeroHashExpr));
            return;
        }

        private void TranslateAbiEncodedFuncCall(FunctionCall funcCall, BoogieExpr lhs)
        {
            var arguments = funcCall.Arguments;
            if (arguments.Count > 2)
            {
                VeriSolAssert(false, $"Variable argument function abi.encodePacked(...) currently supported only for 1 or 2 arguments, encountered  {arguments.Count} arguments");
            }
            var boogieExprs = arguments.ConvertAll(x => TranslateExpr(x));
            var funcName = $"abiEncodePacked{arguments.Count}";
            // hack
            if(arguments[0].TypeDescriptions.TypeString=="address")
                funcName = funcName + "R";
            var abiEncodeFuncCall = new BoogieFuncCallExpr(funcName, boogieExprs);
            currentStmtList.AddStatement(new BoogieAssignCmd(lhs, abiEncodeFuncCall));
            return;
        }

        private void TranslateCallStatement(FunctionCall node, List<BoogieIdentifierExpr> outParams = null)
        {
            VeriSolAssert(outParams == null || outParams.Count == 2, "Number of outPArams for call statement should be 2");
            // only handle call.value(x).gas(y)("") 
            var arg0 = node.Arguments[0].ToString();
            if (!string.IsNullOrEmpty(arg0) && !arg0.Equals("\'\'"))
            {
                currentStmtList.AddStatement(new BoogieSkipCmd(node.ToString()));
                VeriSolAssert(false, "low-level call statements with non-empty signature not implemented..");
            }
						
			BoogieIdentifierExpr tmpVarExpr;
            if (null == outParams)
            {
                tmpVarExpr = MkNewLocalVariableWithType(BoogieType.Bool);
            }
            else
            {
                // almost identical to send(amount)
                tmpVarExpr = outParams[0]; //bool part of the tuple
                if (null == tmpVarExpr)
                {
                    tmpVarExpr = MkNewLocalVariableWithType(BoogieType.Bool);
                }
            }		

            var amountExpr = node.MsgValue != null ? TranslateExpr(node.MsgValue) : new BoogieLiteralExpr(BigInteger.Zero);
            TranslateSendCallStmt(node, tmpVarExpr, amountExpr, true);
            currentExpr = tmpVarExpr;
        }

        private void TranslateTransferCallStmt(FunctionCall node)
        {
            var tmpGas = context.TranslateFlags.InstrumentGas ? MkNewLocalVariableWithType(BoogieType.Int) : null;
            var gasVar = context.TranslateFlags.InstrumentGas ? new BoogieIdentifierExpr("gas") : null;
            
            if (context.TranslateFlags.InstrumentGas)
            {
                var callStipend = new BoogieLiteralExpr(TranslatorContext.CALL_GAS_STIPEND);
                
                currentStmtList.AddStatement(new BoogieAssignCmd(tmpGas, gasVar));
                currentStmtList.AddStatement(new BoogieIfCmd(
                    new BoogieBinaryOperation(BoogieBinaryOperation.Opcode.GT, gasVar, callStipend),
                    BoogieStmtList.MakeSingletonStmtList(new BoogieAssignCmd(gasVar, callStipend)),
                    null));
                
                currentStmtList.AddStatement(new BoogieAssignCmd(tmpGas, new BoogieBinaryOperation(BoogieBinaryOperation.Opcode.SUB, tmpGas, gasVar)));
            }
            
            var amountExpr = TranslateExpr(node.Arguments[0]);
            var memberAccess = node.Expression as MemberAccess;
            var baseExpr = memberAccess.Expression;
            var retVar = MkNewLocalVariableWithType(BoogieType.Bool);

            var callStmt = new BoogieCallCmd("send",
                new List<BoogieExpr>() {new BoogieIdentifierExpr("this"), TranslateExpr(baseExpr), amountExpr},
                new List<BoogieIdentifierExpr>() {retVar});
            
            currentStmtList.AddStatement(callStmt);

            if (!context.TranslateFlags.ModelReverts)
            {
                var assumeStmt = new BoogieAssumeCmd(retVar);
                currentStmtList.AddStatement(assumeStmt);
            }
            else
            {
                var revertLogic = new BoogieStmtList();
                emitRevertLogic(revertLogic);
                currentStmtList.AddStatement(new BoogieIfCmd(new BoogieUnaryOperation(BoogieUnaryOperation.Opcode.NOT, retVar), revertLogic, null));
            }
            
            if (context.TranslateFlags.InstrumentGas)
            {
                currentStmtList.AddStatement(new BoogieAssignCmd(gasVar, new BoogieBinaryOperation(BoogieBinaryOperation.Opcode.ADD, tmpGas, gasVar)));
            }

            return;
        }

        private BoogieCallCmd MkFallbackDispatchCallCmd(FunctionCall node, BoogieExpr amountExpr)
        {
            VeriSolAssert(node.Expression is MemberAccess, $"Expecting a call of the form e.send/e.transfer/e.call, but found {node.ToString()}");
            var memberAccess = node.Expression as MemberAccess;
            var baseExpr = memberAccess.Expression;

            var callStmt = new BoogieCallCmd(
                    "FallbackDispatch",
                    new List<BoogieExpr>() { new BoogieIdentifierExpr("this"), TranslateExpr(baseExpr), amountExpr },
                    new List<BoogieIdentifierExpr>()
                    );
            return callStmt;
        }

        private void TranslateSendCallStmt(FunctionCall node, BoogieIdentifierExpr returnExpr, BoogieExpr amountExpr, bool isCall = false)
        {
            var tmpGas = context.TranslateFlags.InstrumentGas ? MkNewLocalVariableWithType(BoogieType.Int) : null;
            var gasVar = context.TranslateFlags.InstrumentGas ? new BoogieIdentifierExpr("gas") : null;
            
            if (context.TranslateFlags.InstrumentGas && !isCall)
            {
                var callStipend = new BoogieLiteralExpr(TranslatorContext.CALL_GAS_STIPEND);
                
                currentStmtList.AddStatement(new BoogieAssignCmd(tmpGas, gasVar));
                currentStmtList.AddStatement(new BoogieIfCmd(new BoogieBinaryOperation(BoogieBinaryOperation.Opcode.GT, gasVar, callStipend), 
                                                             BoogieStmtList.MakeSingletonStmtList(new BoogieAssignCmd(gasVar, callStipend)),
                                                             null));
                
                currentStmtList.AddStatement(new BoogieAssignCmd(tmpGas, new BoogieBinaryOperation(BoogieBinaryOperation.Opcode.SUB, tmpGas, gasVar)));
            }
            
            var memberAccess = node.Expression as MemberAccess;
            var baseExpr = memberAccess.Expression;
            
            var ins = new List<BoogieExpr>();


            ins.Add(new BoogieIdentifierExpr("this"));
            ins.Add(TranslateExpr(baseExpr));
            ins.Add(amountExpr);
         
            var outs = new List<BoogieIdentifierExpr>();
            outs.Add(returnExpr);
            currentStmtList.AddStatement(new BoogieCallCmd("send", ins, outs));

            if (context.TranslateFlags.InstrumentGas && !isCall)
            {
                currentStmtList.AddStatement(new BoogieAssignCmd(gasVar, new BoogieBinaryOperation(BoogieBinaryOperation.Opcode.ADD, tmpGas, gasVar)));
            }
        }

        private void TranslateNewStatement(FunctionCall node, BoogieExpr lhs)
        {
            VeriSolAssert(node.Expression is NewExpression);
            NewExpression newExpr = node.Expression as NewExpression;

            // define a local variable to temporarily hold the object
            // even though we don't need a temp in some branches, the caller expects a 
            BoogieTypedIdent freshAllocTmpId = context.MakeFreshTypedIdent(BoogieType.Ref);
            BoogieLocalVariable allocTmpVar = new BoogieLocalVariable(freshAllocTmpId);
            boogieToLocalVarsMap[currentBoogieProc].Add(allocTmpVar);
            BoogieIdentifierExpr tmpVarIdentExpr = new BoogieIdentifierExpr(freshAllocTmpId.Name);

            if (newExpr.TypeDescriptions.IsArray())
            {
                // lhs = new A[](5);

                // call tmp := FreshRefGenerator();
                currentStmtList.AddStatement(
                    new BoogieCallCmd(
                        "FreshRefGenerator",
                        new List<BoogieExpr>(),
                        new List<BoogieIdentifierExpr>() {tmpVarIdentExpr}
                        ));
                // length[tmp] = 5
                currentStmtList.AddStatement(
                    new BoogieAssignCmd(
                        new BoogieMapSelect(new BoogieIdentifierExpr("Length"), tmpVarIdentExpr), 
                        TranslateExpr(node.Arguments[0])
                        )
                    );
                // lhs := tmp;
                currentStmtList.AddStatement(new BoogieAssignCmd(lhs, tmpVarIdentExpr));
            }
            else if (newExpr.TypeName is UserDefinedTypeName udt)
            {
                //VeriSolAssert(newExpr.TypeName is UserDefinedTypeName);
                //UserDefinedTypeName udt = newExpr.TypeName as UserDefinedTypeName;

                ContractDefinition contract = context.GetASTNodeById(udt.ReferencedDeclaration) as ContractDefinition;
                VeriSolAssert(contract != null);

                // suppose the statement is lhs := new A(args);

                // call tmp := FreshRefGenerator();
                currentStmtList.AddStatement(
                    new BoogieCallCmd(
                        "FreshRefGenerator",
                        new List<BoogieExpr>(),
                        new List<BoogieIdentifierExpr>() { tmpVarIdentExpr }
                        ));

                // call constructor of A with this = tmp, msg.sender = this, msg.value = tmpMsgValue, args
                string callee = TransUtils.GetCanonicalConstructorName(contract);
                List<BoogieExpr> inputs = new List<BoogieExpr>() {
                   tmpVarIdentExpr,
                   new BoogieIdentifierExpr("this"),
                   new BoogieLiteralExpr(BigInteger.Zero)//assuming msg.value is 0 for new
                };
                foreach (Expression arg in node.Arguments)
                {
                    BoogieExpr argument = TranslateExpr(arg);
                    inputs.Add(argument);
                }
                // assume DType[tmp] == A
                BoogieMapSelect dtypeMapSelect = new BoogieMapSelect(new BoogieIdentifierExpr("DType"), tmpVarIdentExpr);
                BoogieIdentifierExpr contractIdent = new BoogieIdentifierExpr(contract.Name);
                BoogieExpr dtypeAssumeExpr = new BoogieBinaryOperation(BoogieBinaryOperation.Opcode.EQ, dtypeMapSelect, contractIdent);
                currentStmtList.AddStatement(new BoogieAssumeCmd(dtypeAssumeExpr));
                // The assume DType[tmp] == A is before the call as the constructor may do a dynamic 
                // dispatch and the DType[tmp] is unconstrained before the call
                List<BoogieIdentifierExpr> outputs = new List<BoogieIdentifierExpr>();
                currentStmtList.AddStatement(new BoogieCallCmd(callee, inputs, outputs));
                // lhs := tmp;
                currentStmtList.AddStatement(new BoogieAssignCmd(lhs, tmpVarIdentExpr));
            }
        }

        private void TranslateStructConstructor(FunctionCall node, BoogieExpr lhs)
        {
            var structString = node.TypeDescriptions.TypeString.Split(' ')[1];

            // define a local variable to temporarily hold the object
            BoogieTypedIdent freshAllocTmpId = context.MakeFreshTypedIdent(BoogieType.Ref);
            BoogieLocalVariable allocTmpVar = new BoogieLocalVariable(freshAllocTmpId);
            boogieToLocalVarsMap[currentBoogieProc].Add(allocTmpVar);

            // define a local variable to store the new msg.value
            BoogieTypedIdent freshMsgValueId = context.MakeFreshTypedIdent(BoogieType.Int);
            BoogieLocalVariable msgValueVar = new BoogieLocalVariable(freshMsgValueId);
            boogieToLocalVarsMap[currentBoogieProc].Add(msgValueVar);

            BoogieIdentifierExpr tmpVarIdentExpr = new BoogieIdentifierExpr(freshAllocTmpId.Name);
            BoogieIdentifierExpr msgValueIdentExpr = new BoogieIdentifierExpr(freshMsgValueId.Name);
            BoogieIdentifierExpr allocIdentExpr = new BoogieIdentifierExpr("Alloc");

            // suppose the statement is lhs := new A(args);

            // call tmp := FreshRefGenerator();
            currentStmtList.AddStatement(
                new BoogieCallCmd(
                    "FreshRefGenerator",
                    new List<BoogieExpr>(),
                    new List<BoogieIdentifierExpr>() { tmpVarIdentExpr }
                    ));

            // call constructor of A with this = tmp, msg.sender = this, msg.value = tmpMsgValue, args
            string callee = structString + "_ctor"; // TransUtils.GetCanonicalConstructorName(contract);
            List<BoogieExpr> inputs = new List<BoogieExpr>()
            {
                tmpVarIdentExpr,
                new BoogieIdentifierExpr("this"),
                new BoogieLiteralExpr(BigInteger.Zero) // msg.value is 0 
            };
            foreach (Expression arg in node.Arguments)
            {
                BoogieExpr argument = TranslateExpr(arg);
                inputs.Add(argument);
            }
            // assume DType[tmp] == A
            BoogieMapSelect dtypeMapSelect = new BoogieMapSelect(new BoogieIdentifierExpr("DType"), tmpVarIdentExpr);
            BoogieIdentifierExpr contractIdent = new BoogieIdentifierExpr(structString); // contract.Name);
            BoogieExpr dtypeAssumeExpr = new BoogieBinaryOperation(BoogieBinaryOperation.Opcode.EQ, dtypeMapSelect, contractIdent);
            currentStmtList.AddStatement(new BoogieAssumeCmd(dtypeAssumeExpr));
            // The assume DType[tmp] == A is before the call as the constructor may do a dynamic 
            // dispatch and the DType[tmp] is unconstrained before the call
            List<BoogieIdentifierExpr> outputs = new List<BoogieIdentifierExpr>();
            currentStmtList.AddStatement(new BoogieCallCmd(callee, inputs, outputs));
            // lhs := tmp;
            currentStmtList.AddStatement(new BoogieAssignCmd(lhs, tmpVarIdentExpr));
            return;
        }

        private bool IsDynamicArrayPush(FunctionCall node)
        {
            string functionName = TransUtils.GetFuncNameFromFuncCall(node);
            if (functionName.Equals("push"))
            {
                VeriSolAssert(node.Expression is MemberAccess);
                MemberAccess memberAccess = node.Expression as MemberAccess;
                return MapArrayHelper.IsArrayTypeString(memberAccess.Expression.TypeDescriptions.TypeString);
            }
            return false;
        }

        private void TranslateDynamicArrayPush(FunctionCall node)
        {
            VeriSolAssert(node.Expression is MemberAccess);
            VeriSolAssert(node.Arguments.Count == 1);

            MemberAccess memberAccess = node.Expression as MemberAccess;
            BoogieExpr receiver = TranslateExpr(memberAccess.Expression);
            BoogieExpr element = TranslateExpr(node.Arguments[0]);

            BoogieExpr lengthMapSelect = new BoogieMapSelect(new BoogieIdentifierExpr("Length"), receiver);
            // suppose the form is a.push(e)
            // tmp := Length[this][a];
            BoogieTypedIdent tmpIdent = context.MakeFreshTypedIdent(BoogieType.Int);
            boogieToLocalVarsMap[currentBoogieProc].Add(new BoogieLocalVariable(tmpIdent));
            BoogieIdentifierExpr tmp = new BoogieIdentifierExpr(tmpIdent.Name);
            BoogieAssignCmd assignCmd = new BoogieAssignCmd(tmp, lengthMapSelect);
            currentStmtList.AddStatement(assignCmd);

            // M[this][a][tmp] := e;
            BoogieType mapKeyType = BoogieType.Int;
            BoogieType mapValType = MapArrayHelper.InferExprTypeFromTypeString(node.Arguments[0].TypeDescriptions.TypeString);
            BoogieExpr mapSelect = MapArrayHelper.GetMemoryMapSelectExpr(mapKeyType, mapValType, receiver, tmp);
            BoogieAssignCmd writeCmd = new BoogieAssignCmd(mapSelect, element);
            currentStmtList.AddStatement(writeCmd);

            // Length[this][a] := tmp + 1;
            BoogieExpr rhs = new BoogieBinaryOperation(BoogieBinaryOperation.Opcode.ADD, tmp, new BoogieLiteralExpr(1));
            BoogieAssignCmd updateLengthCmd = new BoogieAssignCmd(lengthMapSelect, rhs);
            currentStmtList.AddStatement(updateLengthCmd);

            var isInt = mapValType.Equals(BoogieType.Int);
            if (context.TranslateFlags.InstrumentSums && isInt)
            {
                BoogieExpr sumAccess = getSumAccess(receiver);
                BoogieAssignCmd sumAssign = new BoogieAssignCmd(sumAccess,
                    new BoogieBinaryOperation(BoogieBinaryOperation.Opcode.ADD, sumAccess, element));
                currentStmtList.AddStatement(sumAssign);
            }
            return;
        }
        #endregion

        private void AddUnsignedTypeAssumeCmd(BoogieIdentifierExpr v)
        {
            var predicate = new BoogieBinaryOperation(BoogieBinaryOperation.Opcode.GE, v, new BoogieLiteralExpr(0));
            BoogieAssumeCmd assumeCmd = new BoogieAssumeCmd(predicate);
            currentStmtList.AddStatement(assumeCmd);
        }

        private bool IsExternalFunctionCall(FunctionCall node)
        {
            if (node.Expression is MemberAccess memberAccess)
            {
                if (memberAccess.Expression is Identifier identifier)
                {
                    if (identifier.Name == "this")
                    {
                        return true;
                    }
                    var contract = context.GetASTNodeById(identifier.ReferencedDeclaration) as ContractDefinition;
                    if (contract == null)
                    {
                        return true;
                    }
                }
                else if (memberAccess.Expression is MemberAccess structSelect)
                {
                    //a.b.c.foo(...)
                    //TODO: do we want to check that the contract the struct variable is declared
                    // is not in the "context"? Why this isn't done for IndexAccess?
                    return true;
                }
                else if (memberAccess.Expression.ToString().Equals("msg.sender"))
                {
                    // calls can be of the form "msg.sender.call()" or "msg.sender.send()" or "msg.sender.transfer()"
                    return true;
                }
                else if (memberAccess.Expression is FunctionCall)
                {
                    // TODO: example?
                    return true;
                } else if (memberAccess.Expression is IndexAccess)
                {
                    //a[i].foo(..)
                    return true;
                }
            }
            return false;
        }

        private ContractDefinition IsLibraryFunctionCall(FunctionCall node)
        {
            if (node.Expression is MemberAccess memberAccess)
            {
                if (memberAccess.Expression is Identifier identifier)
                {
                    var contract = context.GetASTNodeById(identifier.ReferencedDeclaration) as ContractDefinition;
                    // a Library is treated as an external function call
                    // we need to do it here as the Lib.Foo, Lib is not an expression but name of a contract
                    if (contract.ContractKind == EnumContractKind.LIBRARY)
                    {
                        return contract;
                    }
                }
            }
            return null;
        }

        private void TranslateExternalFunctionCall(FunctionCall node, List<BoogieIdentifierExpr> outParams = null)
        {
            VeriSolAssert(node.Expression is MemberAccess, $"Expecting a member access expression here {node.Expression.ToString()}");

           MemberAccess memberAccess = node.Expression as MemberAccess;
            if(memberAccess.MemberName.Equals("call"))
            {
                TranslateCallStatement(node, outParams);
                return;
            }
            else if (memberAccess.MemberName.Equals("send"))
            {
                TranslateSendCallStmt(node, outParams[0], TranslateExpr(node.Arguments[0]));
                return;
            }
            else if (IsBuiltInTransferFunc(memberAccess.MemberName, node))
            {
                TranslateTransferCallStmt(node); // this may be unreachable as we already trap transfer directly
                return;
            }

            if (IsUsingBasedLibraryCall(memberAccess))
            {
                TranslateUsingLibraryCall(node, outParams);
                return;
            }

            BoogieExpr receiver = TranslateExpr(memberAccess.Expression);
            BoogieExpr msgValueExpr = null;
            if (node.MsgValue != null)
            {
                msgValueExpr = TranslateExpr(node.MsgValue);
            }
            else
            {
                var msgIdTmp = context.MakeFreshTypedIdent(BoogieType.Int);
                BoogieLocalVariable msgValueVar = new BoogieLocalVariable(msgIdTmp);
                boogieToLocalVarsMap[currentBoogieProc].Add(msgValueVar);
                msgValueExpr = new BoogieIdentifierExpr(msgIdTmp.Name);
            }

            List<BoogieExpr> arguments = new List<BoogieExpr>()
            {
                receiver,
                new BoogieIdentifierExpr("this"),
                msgValueExpr, 
            };

            foreach (Expression arg in node.Arguments)
            {
                BoogieExpr argument = TranslateExpr(arg);
                arguments.Add(argument);
            }

            // TODO: we need a way to determine type of receiver from "x.Foo()"
            // This additional condition is checked in the loop at this call site
            // and was the reason why the code was not abstracted into a single call
            var guard = memberAccess.Expression.ToString() == "this"; 
            TranslateDynamicDispatchCall(node, outParams, arguments, guard, receiver);

            return;
        }

        private void TranslateUsingLibraryCall(FunctionCall node, List<BoogieIdentifierExpr> outParams)
        {
            var memberAccess = node.Expression as MemberAccess;
            VeriSolAssert(memberAccess != null, $"Expecting a member access expression here {node.ToString()}");

            // find the set of libraries that this type is mapped to wiht using
            string typedescr = memberAccess.Expression.TypeDescriptions.TypeString;

            if (memberAccess.Expression.TypeDescriptions.IsStruct() ||
                memberAccess.Expression.TypeDescriptions.IsContract()
                )
            {
                //struct Foo.Bar storage ref
                typedescr = typedescr.Split(" ")[1];
            } 
            if (memberAccess.Expression.TypeDescriptions.IsArray())
            {
                //uint[] storage ref
                typedescr = typedescr.Split(" ")[0];
            }

            //struct Foo.Bar[] storage ref should also work

            VeriSolAssert(context.UsingMap.ContainsKey(currentContract), $"Expect to see a using A for {typedescr} in this contract {currentContract.Name}");

            HashSet<UserDefinedTypeName> usingRange = new HashSet<UserDefinedTypeName>();

            // may need to look into base contracts as well (UsingInBase.sol)
            foreach (int id in currentContract.LinearizedBaseContracts)
            {
                ContractDefinition baseContract = context.GetASTNodeById(id) as ContractDefinition;
                Debug.Assert(baseContract != null);
                if (!context.UsingMap.ContainsKey(baseContract)) continue;
                foreach (var kv in context.UsingMap[baseContract])
                {
                    if (kv.Value.ToString().Equals(typedescr))
                    {
                        usingRange.Add(kv.Key);
                    }
                }
            }
            VeriSolAssert(usingRange.Count > 0, $"Expecting at least one using A for B for {typedescr}");

            string signature = TransUtils.InferFunctionSignature(context, node);
            VeriSolAssert(context.HasFuncSignature(signature), $"Cannot find a function with signature: {signature}");
            var dynamicTypeToFuncMap = context.GetAllFuncDefinitions(signature);
            VeriSolAssert(dynamicTypeToFuncMap.Count > 0);

            //intersect the types with a matching function with usingRange
            var candidateLibraries = new List<Tuple<ContractDefinition, FunctionDefinition>>();
            foreach(var tf in dynamicTypeToFuncMap)
            {
                if (usingRange.Any(x => x.Name.Equals(tf.Key.Name.ToString())))
                {
                    candidateLibraries.Add(Tuple.Create(tf.Key, tf.Value));
                }
            }

            VeriSolAssert(candidateLibraries.Count == 1, $"Expecting a library call to match exactly one function, found {candidateLibraries.Count}");
            var funcDefn = candidateLibraries[0];
            //x.f(y1, y2) in Solidity becomes f_lib(this, this, 0, x, y1, y2)
            var arguments = new List<BoogieExpr>()
            {
                new BoogieIdentifierExpr("this"),
                new BoogieIdentifierExpr("this"),
                new BoogieLiteralExpr(BigInteger.Zero), //msg.value
                TranslateExpr(memberAccess.Expression)
            };
            arguments.AddRange(node.Arguments.Select(x => TranslateExpr(x)));

            var callee = TransUtils.GetCanonicalFunctionName(funcDefn.Item2, context);
            var callCmd = new BoogieCallCmd(callee, arguments, outParams);
            currentStmtList.AddStatement(callCmd);
            // throw new NotImplementedException("not done implementing using A for B yet");
        }

        private bool IsUsingBasedLibraryCall(MemberAccess memberAccess)
        {
            // since we only permit "using A for B" for non-contract types
            // this is sufficient, but not necessary in general since non
            // contracts (including libraries) do not have support methods
            return !memberAccess.Expression.TypeDescriptions.IsContract();
        }

        private void TranslateInternalFunctionCall(FunctionCall node, List<BoogieIdentifierExpr> outParams = null)
        {
            List<BoogieExpr> arguments = TransUtils.GetDefaultArguments();

            // a Library is treated as an external function call
            // we need to do it here as the Lib.Foo, Lib is not an expression but name of a contract
            if (IsLibraryFunctionCall(node) != null)
            {
                arguments[1] = arguments[0]; //msg.sender is also this 
            }

            foreach (Expression arg in node.Arguments)
            {
                BoogieExpr argument = TranslateExpr(arg);    
                arguments.Add(argument);
            }

            // Question: why do we have a dynamic dispatch for an internal call?
            if (node.Kind.Equals("structConstructorCall"))
            {
                // assume the structAssignment is used as: s = S(args);
                TranslateStructConstructor(node, outParams[0]);
            }
            else if (IsDynamicDispatching(node))
            {
                TranslateDynamicDispatchCall(node, outParams, arguments, true, new BoogieIdentifierExpr("this"));
            }
            else if (IsStaticDispatching(node))
            {
                ContractDefinition contract = GetStaticDispatchingContract(node);
                string functionName = TransUtils.GetFuncNameFromFuncCall(node);
                string callee = functionName + "_" + contract.Name;
                BoogieCallCmd callCmd = new BoogieCallCmd(callee, arguments, outParams);

                currentStmtList.AddStatement(callCmd);
            }
            else
            {
                VeriSolAssert(false, $"Unknown type of internal function call: {node.Expression}");
            }
            return;
        }

        private void TranslateDynamicDispatchCall(FunctionCall node, List<BoogieIdentifierExpr> outParams, List<BoogieExpr> arguments, bool condition, BoogieExpr receiver)
        {
            ContractDefinition contractDefn;
            VariableDeclaration varDecl;
            // Solidity internally generates foo() getter for any public state 
            // variable foo in a contract. 
            if (IsGetterForPublicVariable(node, out varDecl, out contractDefn))
            {
                List<ContractDefinition> subtypes = new List<ContractDefinition>(context.GetSubTypesOfContract(contractDefn));
                Debug.Assert(subtypes.Count > 0);

                BoogieExpr lhs = new BoogieMapSelect(new BoogieIdentifierExpr("DType"), receiver);
                BoogieExpr guard = new BoogieBinaryOperation(BoogieBinaryOperation.Opcode.EQ, lhs,
                    new BoogieIdentifierExpr(subtypes[0].Name));

                for (int i = 1; i < subtypes.Count; ++i)
                {
                    BoogieExpr subGuard = new BoogieBinaryOperation(BoogieBinaryOperation.Opcode.EQ, lhs, new BoogieIdentifierExpr(subtypes[i].Name));
                    guard = new BoogieBinaryOperation(BoogieBinaryOperation.Opcode.OR, guard, subGuard);
                }
                
                currentStmtList.AddStatement(new BoogieAssumeCmd(guard));
                VeriSolAssert(outParams.Count == 1, $"Do not support getters for tuples yet {node.ToString()} ");
                string varMapName = TransUtils.GetCanonicalStateVariableName(varDecl, context);
                BoogieMapSelect mapSelect = new BoogieMapSelect(new BoogieIdentifierExpr(varMapName), arguments[0]);
                currentStmtList.AddStatement(new BoogieAssignCmd(outParams[0], mapSelect));
                return;
            }

            Dictionary<ContractDefinition, FunctionDefinition> dynamicTypeToFuncMap;
            string signature = TransUtils.InferFunctionSignature(context, node);
            VeriSolAssert(context.HasFuncSignature(signature), $"Cannot find a function with signature: {signature}");
            dynamicTypeToFuncMap = context.GetAllFuncDefinitions(signature);
            VeriSolAssert(dynamicTypeToFuncMap.Count > 0);

            BoogieIfCmd ifCmd = null;
            BoogieExpr lastGuard = null;
            BoogieCallCmd lastCallCmd = null;
            // generate a single if-then-else statement
            foreach (ContractDefinition dynamicType in dynamicTypeToFuncMap.Keys)
            {
                //ignore the ones those who do not derive from the current contract
                if (condition && !dynamicType.LinearizedBaseContracts.Contains(currentContract.Id))
                    continue;

                FunctionDefinition function = dynamicTypeToFuncMap[dynamicType];
                string callee = TransUtils.GetCanonicalFunctionName(function, context);

                BoogieExpr lhs = new BoogieMapSelect(new BoogieIdentifierExpr("DType"), receiver);
                BoogieExpr rhs = new BoogieIdentifierExpr(dynamicType.Name);
                BoogieExpr guard = new BoogieBinaryOperation(BoogieBinaryOperation.Opcode.EQ, lhs, rhs);
                lastGuard = guard;
                BoogieCallCmd callCmd = new BoogieCallCmd(callee, arguments, outParams);
                lastCallCmd = callCmd;
                BoogieStmtList thenBody = BoogieStmtList.MakeSingletonStmtList(callCmd);
                // BoogieStmtList elseBody = ifCmd == null ? null : BoogieStmtList.MakeSingletonStmtList(ifCmd);
                var falseExpr =  context.TranslateFlags.OmitAssumeFalseForDynDispatch ? true : false;
                BoogieStmtList elseBody = ifCmd == null ? 
                    BoogieStmtList.MakeSingletonStmtList(new BoogieAssumeCmd(new BoogieLiteralExpr(falseExpr))) : 
                    BoogieStmtList.MakeSingletonStmtList(ifCmd);

                ifCmd = new BoogieIfCmd(guard, thenBody, elseBody);
            }

            // optimization: if there is only 1 type that we replace the if with a assume
            if (dynamicTypeToFuncMap.Keys.Count == 1)
            {
                // currentStmtList.AddStatement(new BoogieAssumeCmd(lastGuard));
                currentStmtList.AddStatement(lastCallCmd);
            }
            else
            {
                currentStmtList.AddStatement(ifCmd);
            }
        }

        private bool IsGetterForPublicVariable(FunctionCall node, out VariableDeclaration var, out ContractDefinition contractDefinition)
        {
            var = null;
            contractDefinition = null;
            if (node.Expression is MemberAccess memberAccess)
            {
                if (memberAccess.MemberName.Equals("call"))
                    return false;
                VeriSolAssert(memberAccess.ReferencedDeclaration != null);
                var contractTypeStr = memberAccess.Expression.TypeDescriptions.TypeString;

                if (!context.HasStateVarName(memberAccess.MemberName))
                {
                    return false;
                }
                contractDefinition = context.GetContractByName(contractTypeStr.Substring("contract ".Length));
                VeriSolAssert(contractDefinition != null, $"Expecting a contract {contractTypeStr} to exist in context");

                var = context.GetStateVarByDynamicType(memberAccess.MemberName, contractDefinition);
                return true;
            }

            return false;
        }

        private bool IsStaticDispatching(FunctionCall node)
        {
            if (node.Expression is MemberAccess memberAccess)
            {
                if (memberAccess.Expression is Identifier ident)
                {
                    if (context.GetASTNodeById(ident.ReferencedDeclaration) is ContractDefinition)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private ContractDefinition GetStaticDispatchingContract(FunctionCall node)
        {
            VeriSolAssert(node.Expression is MemberAccess);
            MemberAccess memberAccess = node.Expression as MemberAccess;

            Identifier contractId = memberAccess.Expression as Identifier;
            VeriSolAssert(contractId != null, $"Unknown contract name: {memberAccess.Expression}");

            ContractDefinition contract = context.GetASTNodeById(contractId.ReferencedDeclaration) as ContractDefinition;
            VeriSolAssert(contract != null);
            return contract;
        }

        private bool IsDynamicDispatching(FunctionCall node)
        {
            return node.Expression is Identifier;
        }

        private bool IsTypeCast(FunctionCall node)
        {
            return node.Kind.Equals("typeConversion");
        }


        /// <summary>
        /// 
        /// </summary>
        /// <param name="node"></param>
        /// <param name="lhs"></param>
        /// <param name="isElemenentaryTypeCast">indicates if the cast of to an elementary type, often used in specifications</param>
        private void TranslateTypeCast(FunctionCall node, BoogieExpr lhs, out bool isElemenentaryTypeCast)
        {
            isElemenentaryTypeCast = false; 

            VeriSolAssert(node.Kind.Equals("typeConversion"));
            VeriSolAssert(node.Arguments.Count == 1);
            //VeriSolAssert(node.Arguments[0] is Identifier || node.Arguments[0] is MemberAccess || node.Arguments[0] is Literal || node.Arguments[0] is IndexAccess,
            //    "Argument to a typecast has to be an identifier, memberAccess, indexAccess or Literal");

            // target: lhs := T(expr);
            BoogieExpr exprToCast = TranslateExpr(node.Arguments[0]);

            if (node.Expression is Identifier) // cast to user defined types
            {
                Identifier contractId = node.Expression as Identifier;
                ContractDefinition contract = context.GetASTNodeById(contractId.ReferencedDeclaration) as ContractDefinition;
                VeriSolAssert(contract != null);

                // assume (DType[var] == T);
                List<ContractDefinition> subtypes = new List<ContractDefinition>(context.GetSubTypesOfContract(contract));
                Debug.Assert(subtypes.Count > 0);

                BoogieExpr dtype = new BoogieMapSelect(new BoogieIdentifierExpr("DType"), exprToCast);
                BoogieExpr guard = new BoogieBinaryOperation(BoogieBinaryOperation.Opcode.EQ, dtype,
                    new BoogieIdentifierExpr(subtypes[0].Name));

                for (int i = 1; i < subtypes.Count; ++i)
                {
                    BoogieExpr subGuard = new BoogieBinaryOperation(BoogieBinaryOperation.Opcode.EQ, dtype, new BoogieIdentifierExpr(subtypes[i].Name));
                    guard = new BoogieBinaryOperation(BoogieBinaryOperation.Opcode.OR, guard, subGuard);
                }
                
                currentStmtList.AddStatement(new BoogieAssumeCmd(guard));
                // lhs := expr;
                currentStmtList.AddStatement(new BoogieAssignCmd(lhs, exprToCast));
            }
            else if (node.Expression is ElementaryTypeNameExpression elemType) // cast to elementary types
            {
                isElemenentaryTypeCast = true;
                BoogieExpr rhsExpr = exprToCast;
                // most casts are skips, except address and int cast
                if (elemType.TypeName.Equals("address") || elemType.TypeName.Equals("address payable"))
                {

                    // skip by default, unless we have an integer/hex constant
                    if (exprToCast is BoogieLiteralExpr blit)
                    {
                        if (blit.ToString().Equals("0"))
                        {
                            rhsExpr = (BoogieExpr)new BoogieIdentifierExpr("null");
                        }
                        else
                        {
                            rhsExpr = new BoogieFuncCallExpr("ConstantToRef", new List<BoogieExpr>() { exprToCast });
                        }
                    } else if (node.Arguments[0].TypeDescriptions.IsInt() || node.Arguments[0].TypeDescriptions.IsUint())
                    {
                        rhsExpr = new BoogieFuncCallExpr("ConstantToRef", new List<BoogieExpr>() { exprToCast });
                    }
                }
                var castToInt = Regex.Match(elemType.TypeName, @"[int,uint]\d*").Success;

                if (castToInt && node.Arguments[0].TypeDescriptions.IsAddress())
                {
                    // uint addrInt = uint(addr);
                    // for any other type conversion, make it uninterpreted 
                    rhsExpr = new BoogieFuncCallExpr("BoogieRefToInt", new List<BoogieExpr>() { exprToCast });
                }

                // We do not handle downcasts between unsigned integers, when /useModularArithmetic option is enabled:
                if (context.TranslateFlags.UseModularArithmetic)
                {
                    bool argTypeIsUint = node.Arguments[0].TypeDescriptions.IsUintWSize(node.Arguments[0], out uint argSz);
                    if (argTypeIsUint && elemType.ToString().StartsWith("uint"))
                    {
                        uint castSz = uint.Parse(Extensions.GetNumberFromEnd(elemType.ToString()));
                        if (argSz > castSz)
                        {
                            Console.WriteLine($"Warning: downcasts are not handled with /useModularArithmetic option");
                        }
                    }
                }
                
                // lets update currentExpr with rhsExpr. The caller may update it with the temporary
                currentExpr = rhsExpr; 
                // lhs := expr;
                currentStmtList.AddStatement(new BoogieAssignCmd(lhs, rhsExpr));
            } 
            else
            {
                VeriSolAssert(false, $"Unknown type cast: {node.Expression}");
            }
            return;
        }

        private void VeriSolAssert(bool cond, string message = "")
        {
            if (!cond)
            {
                var contractName = currentContract != null ? currentContract.Name : "Unknown";
                var funcName = currentFunction != null ? currentFunction.Name : "Unknown";
                throw new Exception ($"File {currentSourceFile}, Line {currentSourceLine}, Contract {contractName}, Function {funcName}:: {message}....");
            }
        }

        public override bool Visit(UnaryOperation node)
        {
            preTranslationAction(node);
            BoogieExpr expr = TranslateExpr(node.SubExpression);

            switch (node.Operator)
            {
                case "-":
                case "!":
                    var op = (node.Operator == "-" ? BoogieUnaryOperation.Opcode.NEG : BoogieUnaryOperation.Opcode.NOT);
                    BoogieUnaryOperation unaryExpr = new BoogieUnaryOperation(op, expr);
                    currentExpr = unaryExpr;
                    break;
                case "++":
                case "--":
                    var oper = (node.Operator == "++" ? BoogieBinaryOperation.Opcode.ADD : BoogieBinaryOperation.Opcode.SUB);
                    BoogieExpr rhs = new BoogieBinaryOperation(oper, expr, new BoogieLiteralExpr(1));
                    if (node.Prefix) // ++x, --x
                    {
                        BoogieAssignCmd assignCmd = new BoogieAssignCmd(expr, rhs);
                        currentStmtList.AddStatement(assignCmd);
                        currentExpr = expr;
                    } else // x++, x--
                    {
                        var boogieType = MapArrayHelper.InferExprTypeFromTypeString(node.SubExpression.TypeDescriptions.TypeString);
                        var tempVar = MkNewLocalVariableWithType(boogieType);
                        currentStmtList.AddStatement(new BoogieAssignCmd(tempVar, expr));

                        // Add assume tempVar>=0 for uint
                        AddAssumeForUints(node.SubExpression, tempVar, node.SubExpression.TypeDescriptions);


                        currentExpr = tempVar;
                        BoogieAssignCmd assignCmd = new BoogieAssignCmd(expr, rhs);
                        currentStmtList.AddStatement(assignCmd);
                    }
                    //print the value
                    if (!context.TranslateFlags.NoDataValuesInfoFlag)
                    {
                        var callCmd = new BoogieCallCmd("boogie_si_record_sol2Bpl_int", new List<BoogieExpr>() { expr }, new List<BoogieIdentifierExpr>());
                        callCmd.Attributes = new List<BoogieAttribute>();
                        callCmd.Attributes.Add(new BoogieAttribute("cexpr", $"\"{node.SubExpression.ToString()}\""));
                        currentStmtList.AddStatement(callCmd);
                    }
                    break;
                default:
                    op = BoogieUnaryOperation.Opcode.UNKNOWN;
                    VeriSolAssert(false, $"Unknwon unary operator: {node.Operator}");
                    break;
            }


            return false;
        }

        public override bool Visit(BinaryOperation node)
        {
            preTranslationAction(node);
            BoogieExpr leftExpr = TranslateExpr(node.LeftExpression);
            BoogieExpr rightExpr = TranslateExpr(node.RightExpression);

            BoogieBinaryOperation.Opcode op;
            switch (node.Operator)
            {
                case "+":
                    op = BoogieBinaryOperation.Opcode.ADD;
                    break;
                case "-":
                    op = BoogieBinaryOperation.Opcode.SUB;
                    break;
                case "*":
                    op = BoogieBinaryOperation.Opcode.MUL;
                    break;
                case "**":
                    // Handled below for constants only
                    // TODO: Need to introduce opcode for power operation
                    op = BoogieBinaryOperation.Opcode.MUL;
                    break;
                case "/":
                    op = BoogieBinaryOperation.Opcode.DIV;
                    break;
                case "%":
                    op = BoogieBinaryOperation.Opcode.MOD;
                    break;
                case "==":
                    op = BoogieBinaryOperation.Opcode.EQ;
                    break;
                case "!=":
                    op = BoogieBinaryOperation.Opcode.NEQ;
                    break;
                case ">":
                    op = BoogieBinaryOperation.Opcode.GT;
                    break;
                case ">=":
                    op = BoogieBinaryOperation.Opcode.GE;
                    break;
                case "<":
                    op = BoogieBinaryOperation.Opcode.LT;
                    break;
                case "<=":
                    op = BoogieBinaryOperation.Opcode.LE;
                    break;
                case "&&":
                    op = BoogieBinaryOperation.Opcode.AND;
                    break;
                case "||":
                    op = BoogieBinaryOperation.Opcode.OR;
                    break;
                default:
                    op = BoogieBinaryOperation.Opcode.UNKNOWN;
                    VeriSolAssert(false, $"Unknown binary operator: {node.Operator}");
                    break;
            }

            BoogieExpr binaryExpr;
            if (node.Operator == "**")
            {
                if (node.LeftExpression.TypeDescriptions.IsUintConst(node.LeftExpression, out BigInteger valueLeft, out uint szLeft) &&
                    node.RightExpression.TypeDescriptions.IsUintConst(node.RightExpression, out BigInteger valueRight, out uint szRight))
                {

                    binaryExpr = new BoogieLiteralExpr((BigInteger)Math.Pow((double)valueLeft, (double)valueRight));
                }
                else
                {
                    Console.WriteLine($"VeriSol translation error: power operation for non-constants or with constant subexpressions is not supported; hint: use temps for subexpressions");
                    return false;
                }
            }
            else
            {
                binaryExpr = new BoogieBinaryOperation(op, leftExpr, rightExpr);
            }
            currentExpr = binaryExpr;

            if (context.TranslateFlags.UseModularArithmetic)
            {
                //if (node.Operator == "+" || node.Operator == "-" || node.Operator == "*" || node.Operator == "/" || node.Operator == "**")
                if (node.Operator == "+" || node.Operator == "-" || node.Operator == "*" || node.Operator == "/")
                {
                    if (node.LeftExpression.TypeDescriptions != null && node.RightExpression.TypeDescriptions != null)
                    {
                        var isUintLeft = node.LeftExpression.TypeDescriptions.IsUintWSize(node.LeftExpression, out uint szLeft);
                        var isUintRight = node.RightExpression.TypeDescriptions.IsUintWSize(node.RightExpression, out uint szRight);
                        var isUintConstLeft = node.LeftExpression.TypeDescriptions.IsUintConst(node.LeftExpression, out BigInteger valueLeft, out uint szLeftConst);
                        var isUintConstRight = node.RightExpression.TypeDescriptions.IsUintConst(node.RightExpression, out BigInteger valueRight, out uint szRightConst);
                        // If both operands are literals, do not use "modBpl" for the binary operation:
                        if (isUintLeft && isUintRight)
                        {
                            if (!isUintConstLeft || !isUintConstRight)
                            {
                                VeriSolAssert(szLeft != 0, $"size of uint lhs in binary expr is zero");
                                VeriSolAssert(szRight != 0, $"size of uint rhs in binary expr is zero");
                                BigInteger maxUIntValue = (BigInteger)Math.Pow(2, Math.Max(szLeft, szRight));
                                currentExpr = new BoogieFuncCallExpr("modBpl", new List<BoogieExpr>() { binaryExpr, new BoogieLiteralExpr(maxUIntValue) });
                            }        
                        }
                    }
                }
            }
            
            return false;
        }

        public override bool Visit(Conditional node)
        {
            preTranslationAction(node);
            BoogieExpr guard = TranslateExpr(node.Condition);
            BoogieExpr thenExpr = TranslateExpr(node.TrueExpression);
            BoogieExpr elseExpr = TranslateExpr(node.FalseExpression);

            BoogieITE iteExpr = new BoogieITE(guard, thenExpr, elseExpr);
            currentExpr = iteExpr;

            return false;
        }

        public override bool Visit(IndexAccess node)
        {
            preTranslationAction(node);
            Expression baseExpression = node.BaseExpression;
            Expression indexExpression = node.IndexExpression;

            BoogieType indexType = MapArrayHelper.InferExprTypeFromTypeString(indexExpression.TypeDescriptions.TypeString);
            BoogieExpr indexExpr = TranslateExpr(indexExpression);

            // the baseExpression has an array or mapping type
            BoogieType baseKeyType = MapArrayHelper.InferKeyTypeFromTypeString(baseExpression.TypeDescriptions.TypeString);
            BoogieType baseValType = MapArrayHelper.InferValueTypeFromTypeString(baseExpression.TypeDescriptions.TypeString);
            BoogieExpr baseExpr = null;

            baseExpr = TranslateExpr(baseExpression);
            //if (node.BaseExpression is Identifier identifier)
            //{
            //    baseExpr = TranslateExpr(identifier);
            //}
            //else if (node.BaseExpression is IndexAccess indexAccess)
            //{
            //    baseExpr = TranslateExpr(indexAccess);
            //}
            //else
            //{
            //    VeriSolAssert(false, $"Unknown base in index access: {node.BaseExpression}");
            //}

            BoogieExpr indexAccessExpr = new BoogieMapSelect(baseExpr, indexExpr);
            currentExpr = MapArrayHelper.GetMemoryMapSelectExpr(baseKeyType, baseValType, baseExpr, indexExpr);

            if (context.TranslateFlags.LazyNestedAlloc)
            {
                var valTypeString = MapArrayHelper.GetValueTypeString(baseExpression.TypeDescriptions.TypeString);
                var valIsMapping = MapArrayHelper.IsMappingTypeString(valTypeString);
                var valIsArray = MapArrayHelper.IsArrayTypeString(valTypeString);

                if (valIsArray || valIsMapping)
                {
                    // Check and alloc.
                    BoogieStmtList allocAndInit = new BoogieStmtList();
                    var tmpVarIdentExpr = MkNewLocalVariableWithType(baseValType);
                    allocAndInit.AddStatement(new BoogieCallCmd("FreshRefGenerator",
                        new List<BoogieExpr>(),
                        new List<BoogieIdentifierExpr>() {tmpVarIdentExpr}
                    ));
                    allocAndInit.AddStatement(new BoogieAssignCmd(currentExpr, tmpVarIdentExpr));

                    if (valIsArray)
                    {
                        allocAndInit.AddStatement(new BoogieAssignCmd(new BoogieMapSelect(new BoogieIdentifierExpr("Length"), currentExpr), new BoogieLiteralExpr(0)));
                    }
                    
                    BoogieType nestedValType = MapArrayHelper.InferValueTypeFromTypeString(valTypeString);
                    BoogieType nestedKeyType = MapArrayHelper.InferKeyTypeFromTypeString(valTypeString);

                    var mapName = new BoogieIdentifierExpr(MapArrayHelper.GetMemoryMapName(nestedKeyType, nestedValType));
                    var derefCurrExpr = new BoogieMapSelect(mapName, currentExpr);
                    
                    var qVar1 = QVarGenerator.NewQVar(0, 0);
                    var idxNestedMap = new BoogieMapSelect(derefCurrExpr, qVar1);
                    if (nestedValType == BoogieType.Bool)
                    {
                        if (context.TranslateFlags.QuantFreeAllocs)
                        {
                            allocAndInit.AddStatement(new BoogieAssignCmd(derefCurrExpr, GetCallExprForZeroInit(nestedKeyType, BoogieType.Bool)));
                        }
                        else
                        {
                            var bodyExpr = new BoogieBinaryOperation(
                                BoogieBinaryOperation.Opcode.EQ, 
                                idxNestedMap,
                                new BoogieLiteralExpr(false));
                            var qExpr = new BoogieQuantifiedExpr(true, new List<BoogieIdentifierExpr>() {qVar1},
                                new List<BoogieType>() {nestedKeyType}, bodyExpr);
                            allocAndInit.AddStatement(new BoogieAssumeCmd(qExpr));
                        }
                    }
                    else if (nestedValType == BoogieType.Int)
                    {
                        if (context.TranslateFlags.QuantFreeAllocs)
                        {
                            allocAndInit.AddStatement(new BoogieAssignCmd(derefCurrExpr, GetCallExprForZeroInit(nestedKeyType, BoogieType.Int)));
                        }
                        else
                        {
                            var bodyExpr = new BoogieBinaryOperation(
                                BoogieBinaryOperation.Opcode.EQ, 
                                idxNestedMap,
                                new BoogieLiteralExpr(0));
                            var qExpr = new BoogieQuantifiedExpr(true, new List<BoogieIdentifierExpr>() {qVar1},
                                new List<BoogieType>() {nestedKeyType}, bodyExpr);
                            allocAndInit.AddStatement(new BoogieAssumeCmd(qExpr));
                        }

                        if (context.TranslateFlags.InstrumentSums)
                        {
                            allocAndInit.AddStatement(new BoogieAssignCmd(getSumAccess(currentExpr), new BoogieLiteralExpr(BigInteger.Zero)));
                        }
                    }
                    else if (nestedValType == BoogieType.Ref)
                    {
                        if (context.TranslateFlags.QuantFreeAllocs)
                        {
                            allocAndInit.AddStatement(new BoogieAssignCmd(derefCurrExpr, GetCallExprForZeroInit(nestedKeyType, BoogieType.Ref)));
                        }
                        else
                        {
                            var bodyExpr = new BoogieBinaryOperation(
                                BoogieBinaryOperation.Opcode.EQ, 
                                idxNestedMap,
                                new BoogieIdentifierExpr("null"));
                            var qExpr = new BoogieQuantifiedExpr(true, new List<BoogieIdentifierExpr>() {qVar1},
                                new List<BoogieType>() {nestedKeyType}, bodyExpr);
                            allocAndInit.AddStatement(new BoogieAssumeCmd(qExpr));
                        }
                    }
                    else
                    {
                        throw new Exception("Unexpected type in nested mapping.");
                    }

                    currentStmtList.AddStatement(new BoogieIfCmd(
                        new BoogieBinaryOperation(BoogieBinaryOperation.Opcode.EQ, currentExpr,
                            new BoogieIdentifierExpr("null")), allocAndInit, null));
                }
            }
            return false;
        }

        public override bool Visit(InlineAssembly node)
        {
            preTranslationAction(node);
            Console.WriteLine($"Warning: Inline assembly in function {currentFunction.Name}; replacing function result with non-det value");  
            throw new NotImplementedException("inline assembly");
        }

        public override bool Equals(object obj)
        {
            return obj is ProcedureTranslator translator &&
                   EqualityComparer<BoogieExpr>.Default.Equals(currentExpr, translator.currentExpr) &&
                   EqualityComparer<Dictionary<string, List<BoogieExpr>>>.Default.Equals(ContractInvariants, translator.ContractInvariants);
        }
    }

    static class QVarGenerator
    {
        public static BoogieIdentifierExpr NewQVar(int level, int pos)
        {
            return new BoogieIdentifierExpr($"__i__{level}_{pos}");
        }
    }
}

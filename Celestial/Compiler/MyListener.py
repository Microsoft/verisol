import sys
from antlr4 import *
from CelestialLexer import CelestialLexer
from CelestialParser import CelestialParser
from CelestialParserListener import CelestialParserListener
from FStarCodegen import FStarCodegen
from SolidityCodegen import SolidityCodegen
from Symbol import Symbol
from prettytable import PrettyTable

def revert(errorString, ctx=None):
    if ctx:
        print (str(ctx.start.line) + ":" + str(ctx.start.column) + " : " + errorString)
    else:
        print(errorString)
    exit(1)

class MyListener(CelestialParserListener):

    def __init__(self, FSTCodegen, SolidityCodegen, printSymbolTableFlag, disableFuncBooleanCheck, verificationMode):
        self.FSTCodegen = FSTCodegen
        self.SolidityCodegen = SolidityCodegen
        self.printSymbolTableFlag = printSymbolTableFlag
        self.disableFuncBooleanCheck = disableFuncBooleanCheck
        self.verificationMode = verificationMode        # Added for VeriSol

        # The following variables are not cleared for different contracts
        self.contracts = []
        self.methodsOfContract = {}     # Dict with keys = contract names and values = dict with key as method name and value as tuple of returnvalue and list as params of that method in the contract
        self.fieldsOfContract = {}      # Dict with keys = contract names and values = list of tuples (fieldName, fieldType, isPublic=False), for when a contract can access another contract's public field variables (TODO)

        # The following variables are cleared for different contracts
        self.currentContract = ""
        self.symbols = []
        self.enums = []                 # List of enum types per contract
        self.structs = []               # List of struct types per contract
        self.constructorDefined = False
        self.currentScope = "global"    # String which is either "global" or the name of the method/function/invariant
        self.isParam = False            # True if the walker is currently inside a function call
        self.isSpec = False             # True if the walker is inside a function, invariant or pre/post of a method
        self.methodHasReturn = False    # True if the current method body the walker is in has a return statement
        self.isInPost = False           # True if the walker is currently at the post of a method
        self.reentrancyReverts = []

    def clearCompilerVariables(self):
        """
        Clears all compiler variables that are specific to a single contract
        """

        self.currentContract = ""
        self.symbols = []               # TODO: Don't clear symbols, but have a 'contract' field in Symbol, and include in every symbol lookup the contract name also 
        self.symbols.append(Symbol(_name="keccak256", _type="bytes32", _params=["bytes"], _scope="global", _isMethod=True, _isFunction=True))
        self.symbols.append(Symbol(_name="sha256", _type="bytes32", _params=["bytes"], _scope="global", _isMethod=True, _isFunction=True))
        self.symbols.append(Symbol(_name="ripemd160", _type="bytes20", _params=["bytes"], _scope="global", _isMethod=True, _isFunction=True))
        self.symbols.append(Symbol(_name="ecrecover", _type="address", _params=["bytes32", "uint8", "bytes32", "bytes32"], _scope="global", _isMethod=True, _isFunction=True))
        self.symbols.append(Symbol(_name="sum_mapping", _type="uint", _params=["mapping(address=>uint)"], _scope="global", _isFunction=True))
        self.symbols.append(Symbol(_name="addmod", _type="uint", _params=["uint", "uint", "uint"], _scope="global", _isMethod=True))
        self.symbols.append(Symbol(_name="mulmod", _type="uint", _params=["uint", "uint", "uint"], _scope="global", _isMethod=True))

        self.enums = []
        self.structs = []
        self.constructorDefined = False
        self.currentScope = "global"
        self.isParam = False
        self.isSpec = False
        self.methodHasReturn = False
        self.isInPost = False
        self.reentrancyReverts = []

    # move this function to a typechecker class
    def isBaseType(self, typ):
        if typ in ["int", "uint", "address", "bool", "string"]:
            return True
        else:
            return False


    def enterProgram(self, ctx:CelestialParser.ProgramContext):
        self.SolidityCodegen.enterProgram()

    def exitProgram(self, ctx:CelestialParser.ProgramContext):
        self.SolidityCodegen.exitProgram()

    def enterImportDirective(self, ctx:CelestialParser.ImportDirectiveContext):
        self.SolidityCodegen.writeImportDirective(ctx)

    def enterPragmaDirective(self, ctx:CelestialParser.PragmaDirectiveContext):
        self.SolidityCodegen.writePragmaDirective(ctx)

    def enterDatatype(self, ctx:CelestialParser.DatatypeContext):
        """
        Verifies if maps contain keys that are not maps or arrays themselves
        """

        if ctx.MAP():
            if ctx.datatype(0).MAP() or ctx.datatype(0).arrayType:
                revert ("<ERROR>: Map cannot have map or array as key", ctx)
        elif ctx.INSTMAP():
            if ctx.iden().Iden().getText() not in self.contracts:
                revert ("<ERROR>: inst_map<T> expects contract type as argument", ctx.iden())

    def enterContractDecl(self, ctx:CelestialParser.ContractDeclContext):
        """
        On entering ContractDecl, the contract name is now known
        The contract name is appended to the local list of contracts
        The methodsOfContract dict is initialized with an empty method list for this contract name as key
        The current contract is also set
        This contract name is used to name the F* file and the F* module
        FSTCodegen.enterContract() opens the F* file
        FSTCodegen.enterContract() also writes some boilerplate code to the generated F*
        """

        self.clearCompilerVariables()

        contractName = ctx.name.Iden().getText()
        self.methodsOfContract[contractName] = {}
        self.FSTCodegen.invariantsOfContract[contractName] = []
        self.FSTCodegen.methodsOfContract[contractName] = {}
        self.fieldsOfContract[contractName] = []
        self.currentContract = contractName

        self.FSTCodegen.setModuleName(contractName)
        self.FSTCodegen.enterContract(self.contracts)
        self.SolidityCodegen.enterContractDecl(ctx)

        self.contracts.append(contractName)
        self.FSTCodegen.contracts.append(contractName)
        self.SolidityCodegen.contracts.append(contractName)

    def exitContractDecl(self, ctx:CelestialParser.ContractDeclContext):
        """
        FSTCodegen.exitContract() closes the F* file that was opened
        Also, prints the symbol table to stdout if the printSymbolTable flag is set
        """

        # if not self.constructorDefined:
        #     self.FSTCodegen.writeDefaultConstructor(self.symbols, "constructor")
        self.FSTCodegen.exitContract()
        self.SolidityCodegen.exitContractDecl(ctx)
        # Dev Options
        if (self.printSymbolTableFlag):
            self.printSymbolTable()
    
    def enterContractBody(self, ctx:CelestialParser.ContractBodyContext):
        """
        Sets the current scope to "global"
        Populates the symbol table with fields, functions, invariants and methods
        Also checks if there is any redeclaration and throws if identifiers are redeclared
        For each function, method and invariant, the argument types are also stored.
            They are stored as an array of strings where each string is a type written just as it is in Celestial
            This way of storing datatypes is a convention throughout
        Arguments have a slightly different syntax compared to variable declarations.
            For function declarations, if a map is passed as an argument, we check here that keys are only basic types
            as it is not handled in enterVarDecl()
        Ensures maps and arrays cannot be passed as arguments to  or returned from methods
        FSTCodegen.writeFieldRecords() generates the F* record of contract fields and writes it to the F* file
        FST.Codegen.writeEffectDefinitions() and FSTCodegen.writeGetPutEmitSend() write more boilerplate code to the F* file
        """

        self.currentScope = "global"
        for f in ctx.contractContents():
            if f.constructorDecl():
                self.constructorDefined = True
                break
                
        for f in ctx.contractContents():
            if f.funDecl():
                funcName = f.funDecl().iden().Iden().getText()

                # check if the identifier has been declared before
                if self.checkIdentifierDeclared(funcName):
                    revert ("<ERROR>: Identifier '" + funcName + "' already used", f.funDecl())
                
                # if not declared earlier, write to functions
                self.FSTCodegen.functions[funcName] = f.funDecl().funParamList() # storing the entire object (args can be extracted and typechecked)
            
                # generating function signature
                params = []
                if f.funDecl().funParamList():
                    for parameter in f.funDecl().funParamList().funParam():
                        params.append(parameter.datatype().getText())
                self.symbols.append(Symbol(funcName, "bool", params, "global", False, False, True, False, False, ""))

                # adding it's arguments to the symbol table
                if f.funDecl().funParamList():
                    params = f.funDecl().funParamList().funParam()
                    for param in params:
                        paramName = param.iden().Iden().getText()
                        # we're allowing same param names and field names because functions cannot access fields directly anyway
                        paramType = param.datatype().getText()
                        if self.isMapping(paramType):
                            keyType = self.getMapKeyType(paramType)
                            if (not self.isBaseType(keyType)):
                                revert ("<ERROR>: Keys can only be a basic type", param.datatype())
                            self.symbols.append(Symbol(paramName, paramType, [], funcName, _isParam=True, _isMethod=False, _isFunction=False, _isInvariant=False, _isMap=True, _mapKeyType=keyType))
                        else:
                            self.symbols.append(Symbol(paramName, paramType, [], funcName, _isParam=True, _isMethod=False, _isFunction=False, _isInvariant=False, _isMap=False, _mapKeyType=""))

            elif f.methodDecl():
                methodDeclContext = f.methodDecl()
                if methodDeclContext.name:
                    methodName = methodDeclContext.name.Iden().getText()

                    # check if the identifier has been declared before
                    if self.checkIdentifierDeclared(methodName, "global"):
                        revert ("<ERROR>: Identifier '" + methodName + "' already used", f.methodDecl())

                    # typechecking method argument types
                    if methodDeclContext.methodParamList():
                        for parameter in methodDeclContext.methodParamList().methodParam():
                            if (parameter.datatype().MAP()):
                                revert ("<ERROR>: Methods cannot take maps as arguments", f.methodDecl())
                            elif (parameter.datatype().arrayType):
                                revert ("<ERROR>: Methods cannot take arrays as arguments", f.methodDecl())
                            elif (parameter.datatype().EVENTLOG()):
                                revert ("<ERROR>: Methods cannot take argument of type eventlog", f.methodDecl())
                            elif (parameter.datatype().EVENT()):
                                revert ("<ERROR>: Methods cannot take argument of type event", f.methodDecl())
                    if methodDeclContext.datatype():
                        # typechecking method return types
                        if methodDeclContext.datatype().arrayType or methodDeclContext.datatype().MAP():
                            revert ("<ERROR>: Methods cannot return arrays/maps", f.methodDecl())
                        methodReturnType = methodDeclContext.datatype().getText()
                    else:
                        methodReturnType = "void"

                    # if not declared earlier, write to methods
                    self.FSTCodegen.methods[methodName] = methodDeclContext.datatype() # None if void
                    self.methodsOfContract[self.currentContract][methodName] = ()
                    self.FSTCodegen.methodsOfContract[self.currentContract][methodName] = ()

                    returnVar = ""
                    if methodDeclContext.returnval:
                        returnVar = methodDeclContext.returnval.Iden().getText()
                        self.symbols.append(Symbol(returnVar, methodReturnType, _params=[], _scope=methodName, _isParam=False, _isMethod=False, _isFunction=False, _isInvariant=False, _isMap=False, _mapKeyType="", _isEvent=False))

                    # generating method signature
                    params = []
                    if methodDeclContext.methodParamList():
                        for parameter in methodDeclContext.methodParamList().methodParam():
                            params.append(parameter.datatype().getText())
                    self.symbols.append(Symbol(methodName, methodReturnType, params, "global", _isParam=False, _isMethod=True, _isFunction=False, _isInvariant=False, _isMap=False, _mapKeyType="", _isEvent=False, _returnVar=returnVar))

                    # adding the method to methodsOfContract
                    self.methodsOfContract[self.currentContract][methodName] = (methodReturnType, params)
                    self.FSTCodegen.methodsOfContract[self.currentContract][methodName] = (methodReturnType, params)

                    # adding it's arguments to the symbol table
                    if methodDeclContext.methodParamList():
                        params = methodDeclContext.methodParamList().methodParam()
                        for param in params:
                            paramName = param.iden().Iden().getText()
                            if self.checkIdentifierDeclared(paramName, "global"):
                                revert ("<ERROR>: Variable '" + paramName + "' redeclared", f.methodDecl())
                            paramType = param.datatype().getText()
                            self.symbols.append(Symbol(paramName, paramType, [], methodName, True, False))
                #TODO: Else ensure fallback doesn't have arguments
                # also ensure there is only one of them defined
                if methodDeclContext.spec() and methodDeclContext.spec().rreverts:
                    self.reentrancyReverts.append(methodDeclContext.spec().rreverts)

            elif f.enumDecl():
                enumName = f.enumDecl().name.Iden().getText()

                # check if the identifier has been declared before
                if self.checkIdentifierDeclared(enumName):
                    revert ("<ERROR>: Identifier '" + enumName + "' already used", f.enumDecl())
                
                # if not declared, append to self.enums and write to FSTCodegen.enumTypes and SolidityCodegen.enumTypes
                self.enums.append(enumName)
                self.FSTCodegen.enumTypes.append(enumName)
                self.SolidityCodegen.enumTypes.append(enumName)

                # writing the enumValues for the enumType to self.symbols and FSTCodegen.enumValues
                for iden in f.enumDecl().iden():
                    enumValueName = iden.Iden().getText()
                    if enumValueName == enumName:
                        continue
                    if self.checkIdentifierDeclared(enumValueName):
                        revert ("<ERROR>: Identifier '" + enumValueName + "' already used", f.enumDecl())
                    self.symbols.append(Symbol(_name=enumValueName, _type=enumName, _scope="global", _isEnumValue=True))
                    self.FSTCodegen.enumValues[enumValueName] = enumName

                # writing the enum declaration to Solidity
                self.SolidityCodegen.writeEnumDecl(f.enumDecl())
            
            elif f.varDecl():
                fieldName = f.varDecl().iden().Iden().getText()

                # check if field has already been declared
                if (self.FSTCodegen.existsField(fieldName)):
                    revert ("<ERROR>: Field '" + fieldName + "' already used", f.varDecl())

                # check if the identifier has already been declared before
                if (self.FSTCodegen.existsMethod(fieldName) or self.FSTCodegen.existsInvariant(fieldName) or self.FSTCodegen.existsFunction(fieldName)):
                    revert ("<ERROR>: Identifier '" + fieldName + "' already used", f.varDecl())
                
                # if not declared earlier, write to fields and symbol table
                fieldtypeObject = f.varDecl().datatype()
                fieldType = fieldtypeObject.getText()
                self.FSTCodegen.fields[fieldName] = fieldtypeObject
                if (fieldtypeObject.MAP()):
                    keyType = fieldtypeObject.keyType.getText() # TODO check if map key type handling is done in vardecl only or have to do here also
                    self.symbols.append(Symbol(_name=fieldName, _type=fieldType, _scope="global", _isMap=True, _mapKeyType=keyType))
                elif fieldtypeObject.name:
                    datatypeName = fieldtypeObject.name.Iden().getText()
                    if datatypeName in self.structs:
                        self.symbols.append(Symbol(_name=fieldName, _type=datatypeName, _scope="global", _isStructObject=True))
                    elif datatypeName in self.enums:
                        self.symbols.append(Symbol(_name=fieldName, _type=datatypeName, _scope="global"))
                    elif datatypeName in self.contracts[0:self.contracts.index(self.currentContract)]:
                        self.symbols.append(Symbol(_name=fieldName, _type=datatypeName, _scope="global", _isContractObject=True))
                else:
                    self.symbols.append(Symbol(_name=fieldName, _type=fieldType, _scope="global"))
                self.fieldsOfContract[self.currentContract].append((fieldName, fieldType, False))
                if f.varDecl().expr():
                    # TODO: Typecheck this expression, and check if its type matches the field type
                    self.FSTCodegen.initialFieldValues[fieldName] = f.varDecl().expr()

            elif f.structDecl():
                structContext = f.structDecl()
                structName = structContext.name.Iden().getText()

                # check if the identifier has been declared before
                if self.checkIdentifierDeclared(structName):
                    revert ("Identifier '" + structName + "' already used", structContext)

                structFieldTypes = []
                # checking if the field names have been used before, and adding them to the symbol table
                numberOfStructFields = len(structContext.datatype())
                for i in range(numberOfStructFields):
                    structFieldName = structContext.iden(i+1).Iden().getText()
                    if self.checkIdentifierDeclared(structFieldName):
                        revert ("Identifier '" + structFieldName + "' already used", structContext)
                    structFieldType = structContext.datatype(i).getText()
                    structFieldTypes.append(structFieldType)
                    if "mapping" in structFieldType:
                        keyType = self.getMapKeyType(structFieldType)
                        if (not self.isBaseType(keyType)):
                            revert ("<ERROR>: Keys can only be a basic type", structContext)
                        self.symbols.append(Symbol(_name=structFieldName, _scope="global", _type=structFieldType, _isMap=True, _mapKeyType=keyType, _isStructField=True, _fieldOfStruct=structName))
                    else:
                        self.symbols.append(Symbol(_name=structFieldName, _scope="global", _type=structFieldType, _isStructField=True, _fieldOfStruct=structName))
                
                # adding the struct itself to the symbol table and self.structs
                self.structs.append(structName)
                self.symbols.append(Symbol(_name=structName, _params=structFieldTypes, _scope="global", _type=structName, _isStructType=True))
                self.SolidityCodegen.writeStruct(structContext)

            elif f.invariantDecl():
                invariantName = f.invariantDecl().iden().Iden().getText()

                # check if the identifier has been declared before
                if self.checkIdentifierDeclared(invariantName):
                    revert ("<ERROR>: Identifier '" + invariantName + "' already used", f.enumDecl())
                
                # if not declared earlier, write to invariants and symbol table
                self.FSTCodegen.invariants.append(invariantName)
                self.FSTCodegen.invariantsOfContract[self.currentContract].append(invariantName)
                self.symbols.append(Symbol(invariantName, "logical", [], "global", False, False, False, True))

            elif f.constructorDecl(): #TODO: Move to enterConstructorDecl
                methodDeclContact = f.constructorDecl()

                # check if constructor already defined
                if self.checkIdentifierDeclared("constructor"): # polymorphism not allowed for now
                    revert ("<ERROR>: Constructor already declared", f.constructorDecl())

                # typechecking method argument types
                if methodDeclContact.methodParamList():
                    for parameter in methodDeclContact.methodParamList().methodParam():
                        if (parameter.datatype().MAP()):
                            revert ("<ERROR>: Methods cannot take maps as arguments", f.constructorDecl())
                        elif (parameter.datatype().arrayType):
                            revert ("<ERROR>: Methods cannot take arrays as arguments", f.constructorDecl())
                        elif (parameter.datatype().EVENTLOG()):
                            revert ("<ERROR>: Methods cannot take argument of type eventlog", f.constructorDecl())
                        elif (parameter.datatype().EVENT()):
                            revert ("<ERROR>: Methods cannot take argument of type event", f.constructorDecl())
                # if methodDeclContact.datatype():
                #     # typechecking method return types
                #     if methodDeclContact.datatype().arrayType or methodDeclContact.datatype().MAP():
                #         revert ("<ERROR>: Methods cannot return arrays/maps")
                #     methodReturnType = methodDeclContact.datatype().getText()
                # else:
                #     methodReturnType = "void"

                self.FSTCodegen.methods["constructor"] = methodDeclContact

                returnVar = ""
                # No return var in constructor
                # if methodDeclContact.returnval:
                #     returnVar = methodDeclContact.returnval.Iden().getText()
                #     self.symbols.append(Symbol(returnVar, methodReturnType, _params=[], _scope=methodName, _isParam=False, _isMethod=False, _isFunction=False, _isInvariant=False, _isMap=False, _mapKeyType="", _isEvent=False))

                # generating method signature
                params = []
                if methodDeclContact.methodParamList():
                    for parameter in methodDeclContact.methodParamList().methodParam():
                        params.append(parameter.datatype().getText())
                self.symbols.append(Symbol("constructor", "void", params, "global", _isParam=False, _isMethod=True, _isFunction=False, _isInvariant=False, _isMap=False, _mapKeyType="", _isEvent=False, _returnVar=returnVar))

                # adding the method to methodsOfContract
                self.methodsOfContract[self.currentContract]["constructor"] = ("void", params)
                self.FSTCodegen.methodsOfContract[self.currentContract]["constructor"] = ("void", params)

                # adding it's arguments to the symbol table
                if methodDeclContact.methodParamList():
                    params = methodDeclContact.methodParamList().methodParam()
                    for param in params:
                        paramName = param.iden().Iden().getText()
                        if self.checkIdentifierDeclared(paramName, "global"):
                            revert ("<ERROR>: Variable '" + paramName + "' redeclared", f.constructorDecl())
                        paramType = param.datatype().getText()
                        self.symbols.append(Symbol(paramName, paramType, [], "constructor", _isParam=True, _isMethod=False))

            elif f.eventDecl():
                eventName = f.eventDecl().iden().Iden().getText()

                # check if the identifier has been declared before
                if self.checkIdentifierDeclared(eventName):
                    revert("<ERROR>: Identifier '" + eventName + "' already used", f.eventDecl())

                # if not declared earlier, write to events and symbol table
                self.FSTCodegen.events.append(eventName)
                
                params = []
                for datatype in f.eventDecl().datatype():
                    if datatype.MAP():
                        revert ("<ERROR>: Events cannot take a map as argument", f.eventDecl())
                    params.append(datatype.getText())
                self.symbols.append(Symbol(eventName, "event", params, "global", _isParam=False, _isMethod=False, _isFunction=False, _isInvariant=False, _isMap=False, _mapKeyType="", _isEvent=True))
                self.SolidityCodegen.writeEventDecl(f.eventDecl())

        self.FSTCodegen.writeEventDeclarations()
        self.FSTCodegen.writeEnums()
        self.FSTCodegen.structs = self.structs
        self.SolidityCodegen.structs = self.structs
        self.FSTCodegen.writeStructDecl(self.structs, self.symbols)
        self.FSTCodegen.writeFieldRecord(self.symbols)
        self.FSTCodegen.writeAddressAndLiveness()
        self.FSTCodegen.writeFieldGetterSetters(self.symbols)

    def enterUsingForDecl(self, ctx:CelestialParser.UsingForDeclContext):
        self.SolidityCodegen.writeUsingForDecl(ctx, self.symbols)

    def enterVarDecl(self, ctx:CelestialParser.VarDeclContext):
        """
        Checking if variables have been declared before
        This check is only for fields and local variables; function/method arguments are handled separately inside `enterContractBody`
        Variable names cannot be resued between fields and local variables/arguments
        """

        varName = ctx.iden().Iden().getText()
        if (self.currentScope != "global"):
            for sym in self.symbols:
                if (sym.name == varName and (sym.scope == self.currentScope or sym.scope == "global")):
                    revert ("<ERROR>: Variable redeclared", ctx)

                if ctx.datatype().EVENT() or ctx.datatype().EVENTLOG():
                    revert ("<ERROR>: Cannot create variable of type event or eventlog inside method", ctx)
                if ctx.expr():
                    rvalueType = self.exprType (ctx.expr(), self.currentScope)
                    if ctx.datatype().getText() != rvalueType:
                        if not (ctx.datatype().getText() == "address" and rvalueType in self.contracts):
                            revert ("<ERROR>: Assignment type mismatch", ctx)
                
            self.symbols.append(Symbol(_name=varName, _type=ctx.datatype().getText(), _scope=self.currentScope, _isParam=self.isParam, _isLocal=True))
            self.FSTCodegen.writeVariable(ctx, self.symbols, self.currentScope)

        self.SolidityCodegen.enterVarDecl(ctx, self.currentScope, self.symbols)

    def enterInvariantDecl(self, ctx:CelestialParser.InvariantDeclContext):
        """
        The expression inside an invariant should typecheck to boolean
        Writes the invariant to the F* file
        """

        invariantName = ctx.iden().Iden().getText()
        self.currentScope = invariantName
        self.isSpec = True
        self.FSTCodegen.writeInvariant(invariantName)

    def exitInvariantDecl(self, ctx:CelestialParser.InvariantDeclContext):
        """
        Sets the current scope back to "global" after exiting the invariant body
        """
        
        self.currentScope = "global"
        self.isSpec = False

    def enterInvariantBody(self, ctx:CelestialParser.InvariantBodyContext):
        """
        Checks if the expression inside the invariant typechecks to boolean
        Writes the invariant body to the F* file
        """

        expr = ctx.getChild(1)
        if (self.exprType(expr, self.currentScope) != "bool"):
            revert ("<ERROR>: Invariants should have pure expressions of boolean type", ctx.getChild(1))
        self.FSTCodegen.writeInvariantBody(expr, self.symbols, self.currentScope)
        if self.verificationMode == "VeriSol":
            self.SolidityCodegen.appendInvariantBody(expr, self.symbols, self.currentScope)     #Added for VeriSol

    def enterFDecl(self, ctx:CelestialParser.FunDeclContext):
        """
        Sets current scope to the function name on entering the function
            Needed to typecheck expressions inside the function body
        Writes the function declaration to the F* file
            The rest of the function (body) is typechecked to boolean and written to the F* file in `enterFunctionBody`
        """

        funcName = ctx.iden().Iden().getText()
        self.currentScope = funcName
        self.isSpec = True
        self.FSTCodegen.writeFunction(ctx, self.symbols)

    def exitFDecl(self, ctx:CelestialParser.FunDeclContext):
        """
        Sets current scope back to "global" after exiting the function body
        """

        self.currentScope = "global"
        self.isSpec = False

    def enterFunctionBody(self, ctx:CelestialParser.FunctionBodyContext):
        """
        Checks if the expression inside the function typechecks to boolean
        Writes the function body to the F* file
        disableFuncBooleanCheck is a flag to disable the above check to test expressions during compiler development
            Expressions to be tested can be written inside a function no matter what they typecheck to
        """

        expr = ctx.getChild(1)
        if not self.disableFuncBooleanCheck: # Dev Options
            functionExprType = self.exprType(expr, self.currentScope)
            if (functionExprType != "bool"):
                revert ("<ERROR>: Functions should have pure expressions of boolean type", ctx)
        self.FSTCodegen.writeFunctionBody(expr, self.symbols, self.currentScope)

    def enterFunParamList(self, ctx:CelestialParser.FunParamListContext):
        self.isParam = True # no need for this because the param declaration is different from a normal variable dec
                            # hence we are handling (adding to symtable) param variables differently anyway
                            # if it was the same, then ALL variables are added to symtable in enterVarDecl() and this line sets whether it is a param or not

    def exitFunParamList(self, ctx:CelestialParser.FunParamListContext):
        self.isParam = False

    def enterConstructorDecl(self, ctx:CelestialParser.ConstructorDeclContext):
        """
        Sets the current scope to "constructor"
        Disallows writing pre-conditions for constructors
        Checks if the post condition is a boolean expressions, and boolean formulae of function calls
        Writes the constructor declaration to the F* file
        Multiple constructor check is done in enterContractBody
        """

        self.currentScope = "constructor"
        self.isInPost = False

        if ctx.spec():
            if ctx.spec().pre:
                # We allow pre-conditions for constructor
                preType = self.exprType(ctx.spec().pre, self.currentScope)
                if (preType != "bool"):
                    revert ("<ERROR>: Post-conditions can only be a boolean expression", ctx.spec().pre)
                # if not self.checkPrePost(ctx.spec().pre):
                #     revert ("<ERROR>: Post-conditions are boolean formulae of functions", ctx.spec().pre)
                # revert ("<ERROR>: Constructor cannot have a pre-condition")
            if ctx.spec().post:
                self.isInPost = True
                postType = self.exprType(ctx.spec().post, self.currentScope)
                if (postType != "bool"):
                    revert ("<ERROR>: Post-conditions can only be a boolean expression", ctx.spec().post)
                # if not self.checkPrePost(ctx.spec().post):
                #     revert ("<ERROR>: Post-conditions are boolean formulae of functions", ctx.spec().post)
            if ctx.spec().reverts:
                self.isInPost = False
                txrevertsType = self.exprType(ctx.spec().reverts, self.currentScope)
                if (txrevertsType != "bool"):
                    revert ("<ERROR>: Reverts-conditions can only be a boolean expression", ctx.spec().reverts)
                # if not self.checkPrePost(ctx.spec().reverts):
                #     revert ("<ERROR>: Reverts-conditions are boolean formulae of functions", ctx.spec().reverts)
            if ctx.spec().DEBIT() and not ctx.spec().CREDIT():
                revert ("<ERROR>: A constructor cannot debit without credit", ctx.spec().DEBIT())

        self.FSTCodegen.writeConstructor(self.symbols, self.currentScope, ctx)
        self.SolidityCodegen.writeConstructor(ctx)

    def exitConstructorDecl(self, ctx:CelestialParser.ConstructorDeclContext):
        """
        Sets current scope back to "global" after exiting the constructor body
        """
        
        # self.FSTCodegen.exitWriteConstructor(ctx)
        self.SolidityCodegen.exitWriteConstructor()
        self.currentScope = "global"

    def enterMDecl(self, ctx:CelestialParser.MethodDeclContext):
        """
        Sets the current scope to the method name
        Checks if the pre and post conditions are boolean expressions, and boolean formulae of functions
        Verifies the modifies clause to contain only field names
        Writes the method declaration to the F* file
        Writes the method spec to the F* file
        """

        if ctx.name:
            methodName = ctx.name.Iden().getText()
        elif ctx.RECEIVE():
            if self.verificationMode == "VeriSol":
                revert ("<ERROR>: VeriSol doesn't support receive function. Uptil Solidity version 0.5.10 is supported!", ctx.RECEIVE())
            else:
                methodName = "receive"
        else:
            methodName = "fallback"
        self.currentScope = methodName

        if ctx.spec():
            self.isSpec = True
            if ctx.spec().pre:
                self.isInPost = False
                preType = self.exprType(ctx.spec().pre, self.currentScope)
                if (preType != "bool"):
                    revert ("<ERROR>: Pre-conditions can only be a boolean expression", ctx.spec().pre)
                # if not self.checkPrePost(ctx.spec().pre):
                #     revert ("<ERROR>: Pre-conditions are boolean formulae of functions", ctx.spec().pre)
            if ctx.spec().post:
                self.isInPost = True # not useful: 'error'
                postType = self.exprType(ctx.spec().post, self.currentScope)
                if (postType != "bool"):
                    revert ("<ERROR>: Post-conditions can only be a boolean expression", ctx.spec().post)
                # if not self.checkPrePost(ctx.spec().post):
                #     revert ("<ERROR>: Post-conditions are boolean formulae of functions", ctx.spec().post)
            if ctx.spec().reverts:
                self.isInPost = False
                txrevertsType = self.exprType(ctx.spec().reverts, self.currentScope)
                if (txrevertsType != "bool"):
                    revert ("<ERROR>: Reverts-conditions can only be a boolean expression", ctx.spec().reverts)
                # if not self.checkPrePost(ctx.spec().reverts):
                #     revert ("<ERROR>: Reverts-conditions are boolean formulae of functions", ctx.spec().reverts)
            self.isSpec = False
        if (ctx.modifies):
            for rvalue in ctx.modifies.rvalue():
                if (rvalue.expr().getText() != "balance" and rvalue.expr().getText() != "log" and not self.FSTCodegen.existsField(rvalue.expr().getText())):
                    revert ("<ERROR>: 'modifies' clause takes in only contract state variables", ctx.modifies)

        self.FSTCodegen.writeMethod(ctx, self.symbols, self.currentScope) # TODO: CurrentScope should be 'invariant' here
        self.SolidityCodegen.writeMethod(ctx)

    def exitMDecl(self, ctx:CelestialParser.MDeclContext):
        """
        Sets current scope back to "global" after exiting the method body
        """

        self.SolidityCodegen.exitWriteMethod()
        self.currentScope = "global"

    def enterMethodBody(self, ctx:CelestialParser.MethodBodyContext):
        """
        Disallows hanging block statements (grammar allows it)
        Typecheck the return value and write it to F*
        """
        
        self.methodHasReturn = False
        if ctx.statement():
            for statement in ctx.statement():
                if statement.LBRACE():
                    revert ("<ERROR>: Block statements only for if and for statment", ctx)
        # if ctx.varDecl():
        #     for variable in ctx.varDecl():
        #         varName = variable.iden().Iden().getText()
        #         varTypeContext = variable.datatype()
        #         if varTypeContext.EVENT() or varTypeContext.EVENTLOG():
        #             revert ("<ERROR>: Cannot create variable of type event or eventlog inside method", ctx)
        #         if variable.expr():
        #             if varTypeContext.getText() != self.exprType(variable.expr(), self.currentScope):
        #                 revert ("<ERROR>: ASsignment type mismatch")
        #         # self.symbols.append(Symbol(_name=varName, _type=varTypeContext.getText(), _scope=self.currentScope, _isLocal=True))
        #         self.FSTCodegen.writeVariable(variable, self.symbols, self.currentScope)
        
    def exitMethodBody(self, ctx:CelestialParser.MethodBodyContext):
        # if (not self.methodHasReturn):
        #     self.FSTCodegen.writeReturn(self.currentScope)
        currentMethodReturnType = self.getIdenType(self.currentScope, self.currentScope)
        valueReturned = ctx.returnStatement().expr()
        if valueReturned:
            if currentMethodReturnType == "void":
                revert ("<ERROR>: Not expecting a return value", ctx)
            else:
                valueReturnedType = self.exprType(valueReturned, self.currentScope, inFunctionCall=False)
                if valueReturnedType != currentMethodReturnType:
                    revert("<ERROR>: Return expects a value of type " + currentMethodReturnType, ctx)
                else:
                    self.FSTCodegen.writeReturnStatement(ctx.returnStatement(), self.symbols, self.currentScope, isVoid=False)
        else:
            if currentMethodReturnType != "void":
                revert ("<ERROR>: Expects a return value of type " + currentMethodReturnType, ctx.returnStatement())
            else:
                # if self.currentScope != "constructor":
                self.FSTCodegen.writeReturnStatement(ctx.returnStatement(), self.symbols, self.currentScope, isVoid=True)

        # for sym in self.symbols:
        #     if sym.scope == self.currentScope and sym.isLocal:
        #         self.symbols.remove(sym)

    def enterReturnStatement(self, ctx:CelestialParser.ReturnStatementContext):
        self.SolidityCodegen.writeReturnStatement(ctx, self.symbols, self.currentScope)

    # def checkPrePost(self, ctx:CelestialParser.ExprContext):
    #     if ctx.method or (ctx.primitive() and ctx.primitive().BoolLiteral()):
    #         return True
    #     if ctx.LAND() or ctx.LOR():
    #         return self.checkPrePost(ctx.expr(0)) and self.checkPrePost(ctx.expr(1))
    #     elif ctx.LNOT():
    #         return self.checkPrePost(ctx.expr(0))
    #     elif ctx.LPAREN(0) and ctx.getChildCount() == 3: # (expr)
    #         return self.checkPrePost(ctx.expr(0))
    #     else:
    #         return False

    def enterSpec (self, ctx:CelestialParser.SpecContext):
        """
        Sets isSpec to True
        """

        self.isSpec = True
    #     if ctx.pre:
    #         preType = self.exprType(ctx.pre, self.currentScope)
    #         if (preType != "bool"):
    #             revert ("<ERROR>: Pre-conditions can only be a boolean expression", ctx)
    #         if not self.checkPrePost(ctx.pre):
    #             revert ("<ERROR>: Pre-conditions are boolean formulae of functions", ctx)
    #     if ctx.post:
    #         self.isInPost = True # not useful: 'error'
    #         postType = self.exprType(ctx.post, self.currentScope)
    #         if (postType != "bool"):
    #             revert ("<ERROR>: Post-conditions can only be a boolean expression", ctx)
    #         if not self.checkPrePost(ctx.post):
    #             revert ("<ERROR>: Post-conditions are boolean formulae of functions", ctx)

    def exitSpec (self, ctx:CelestialParser.SpecContext):
        """
        Sets isSpec to False
        """
        self.isSpec = False
        self.isInPost = False

    def enterMethodParamList(self, ctx:CelestialParser.MethodParamListContext):
        self.isParam = True # no need for this because the param declaration is different from a normal variable dec
                            # hence we are handling (adding to symtable) param variables differently (in enterContractBody itself) anyway
                            # if it was the same, then ALL variables are added to symtable in enterVarDecl() and this line sets whether it is a param or not

    def exitMethodParamList(self, ctx:CelestialParser.MethodParamListContext):
        self.isParam = False

    def enterExpr(self, ctx:CelestialParser.ExprContext):
        """
        Performing the following checks:
            Invariants only operate on fields
            Functions only operate on its arguments (pure)
            Methods only operate on fields and its arguments
            Enums are allowed in all
        """
        # flag = False
        # if (ctx.primitive() and ctx.primitive().iden() and not ctx.primitive().NEW()):
        #     varName = ctx.primitive().iden().Iden().getText()
        #     if varName not in self.enums: #TODO: Not a clean way of doing this
        #         isMFI = self.getIsMethodFuncInv(self.currentScope)
        #         if isMFI == "invariant":
        #             if not self.FSTCodegen.existsField(varName): # TODO: enums can be used
        #                 revert ("<ERROR>: Use of variable other than field variables inside invariants is not allowed", ctx)
        #         elif isMFI == "function":
        #             for sym in self.symbols:
        #                 if sym.name == varName and sym.scope == self.currentScope:
        #                     flag = True
        #                     break
        #             if not flag:
        #                 revert ("<ERROR>: Variable " + varName + " not an argument to the function", ctx)
        #         elif isMFI == "method":
        #             for sym in self.symbols:
        #                 if sym.name == varName and sym.scope in [self.currentScope, "global"]:
        #                     flag = True
        #                     break
        #             if not flag and varName != "eTransfer":
        #                 revert ("<ERROR>: Variable " + varName + " not declared", ctx)
        
        """
        Ensures functions and invariants cannot be called from inside methods
        """
        if (not self.isSpec and ctx.method): #and ctx.getChildCount() == 4):
            currentScopeType = self.getIsMethodFuncInv(self.currentScope)
            callee = ctx.method.Iden().getText()
            calleeType = self.getIsMethodFuncInv(callee)
            if ((callee not in ["keccak256", "sha256", "ripemd160", "ecrecover"])
                    and (calleeType in ["function", "invariant"])):
                revert ("<ERROR>: Methods cannot be called inside functions or invariants", ctx)

        """
        Ensures that only functions can be called in the 'pre' and 'post'
        """
        if (self.isSpec and ctx.method):
            callee = ctx.method.Iden().getText()
            calleeType = self.getIsMethodFuncInv(callee)
            if ((calleeType == "method" or calleeType == "invariant")
                and (callee not in ["keccak256", "sha256", "ripemd160", "ecrecover"])):
                revert ("<ERROR>: Methods and invariants cannot be called from specifications", ctx.method)

        """
        Ensures that ite() can only be used in spec
        """
        if (not self.isSpec and ctx.ITE()):
            revert ("<ERROR>: ite() cannot be used outside spec", ctx)

        # TODO: Add ==> operator
        
        # Typechecks the expression (it is redundant because it is recursive, but not optimizing the compiler at this stage)
        # self.exprType(ctx, self.currentScope)

    def isArray(self, datatypeString):
        """
        Given a string representing a type, returns if it
            is an array or not
        """

        if datatypeString[-2:] == "[]":
            return True
        return False

    def isArrayOf(self, datatypeString):
        """
        Given a string of the form "type[]", returns 'type'
        """

        start = datatypeString.find("[")
        return datatypeString[0:start]

    def isMapping(self, datatypeString):
        """
        Given a string representing a type, returns if it
            is an array or not
        """

        if datatypeString[0:7] == "mapping":
            return True
        return False

    def getMapKeyType(self, mappingString):
        """
        Given a string of the form 'mapping(type1 => type2),
            returns the type of the key of the map 'type1'
        """

        start = mappingString.find("(") + 1
        end = mappingString.find("=>")
        return mappingString[start:end]

    def getMapValueType(self, mappingString):
        """
        Given a string of the form 'mapping(type1 => type2),
            returns the type of the value of the map 'type2'
        """

        start = mappingString.find("=>") + 2
        end = mappingString.rfind(")")
        return mappingString[start:end]

    # move the below function to a typechecker class?
    def exprType(self, ctx, scope, inFunctionCall=False):
        """
        Given an expression, returns the type of expression by recursively typechecking its children expressions
        The type is represented as a string (for ex: "array[int]", "map[address, uint]")
        For array or map access, the type returned is the value's type
        For variable/field/method, the symbol table is looked up for the type
        Otherwise, the type is determined based on the operator and operands
        Expressions are also typechecked here (for ex, operators take operands of the same type, etc.)
        The type is inferred (because it can be int or uint) for integer literals based on the surrounding context
        """

        if ctx.getChildCount() == 0:
            return ""
        c = ctx.getChild(0)

        # primitive
        if (isinstance (c, CelestialParser.PrimitiveContext)):
            if c.BoolLiteral():
                return "bool"
            elif c.IntLiteral():
                return "int"
            elif c.NullLiteral():
                return "address"
            elif c.StringLiteral():
                return "string"
            elif c.VALUE() or c.BDIFF() or c.BGASLIMIT() or c.BNUMBER() or c.BTIMESTAMP() or c.TXGASPRICE():
                flag = self.getIsMethodFuncInv(self.currentScope)
                # if flag != "method":
                #     revert ("<ERROR>: Cannot use '" + c.getText() + "' outside methods ", ctx)
                return "uint"
            elif c.BALANCE():
                flag = self.getIsMethodFuncInv(self.currentScope)
                if flag != "method" and flag != "invariant":
                    revert ("<ERROR>: Cannot use 'balance' outside methods, pre-post and invariants", ctx)
                return "uint"
            elif c.SENDER() or c.TXORIGIN() or c.BCOINBASE():
                flag = self.getIsMethodFuncInv(self.currentScope)
                # if flag != "method":
                #     revert ("<ERROR>: Cannot use '" + c.getText() + "' outside methods", ctx)
                return "address"
            elif c.LOG():
                flag = self.getIsMethodFuncInv(self.currentScope)
                if flag != "method" and flag != "invariant" and not self.isSpec:
                    revert ("<ERROR>: Cannot use 'log' outside pre-post and invariants", ctx)
                return "eventlog"
            elif c.THIS() or c.ADDR():
                return "address" # TODO: ContractType? or return scope
            elif c.NEW():
                if not (self.FSTCodegen.existsField(c.iden().Iden().getText())):
                    revert ("<ERROR>: new() used only for fields", ctx)
                if not self.isInPost:
                    revert ("<ERROR>: new() cannot be used in the pre or reverts conditions", ctx) 
                return self.getIdenType(c.iden().Iden().getText(), scope)
            elif c.INT_MIN() or c.INT_MAX():
                return "int"
            elif c.UINT_MAX():
                return "uint"
            else: # getIdenType also checks if the variable has been declared or not
                flag = False
                varName = c.iden().Iden().getText()
                for symbol in self.symbols:
                    if symbol.name == varName and symbol.isEnumValue:
                        revert ("<ERROR>: enum value '" + varName + "' should be prefixed with '" + symbol.type + ".'", c.iden())
                if varName not in self.enums: #TODO: Not a clean way of doing this
                    isMFI = self.getIsMethodFuncInv(self.currentScope)
                if isMFI == "invariant":
                    if not self.FSTCodegen.existsField(varName) and not self.getSym(varName).isQuantifierVar  : # TODO: enums can be used
                        revert ("<ERROR>: Use of variable other than field variables inside invariants is not allowed", ctx)
                elif isMFI == "function":
                    if self.checkIdentifierDeclared(varName, self.currentScope):
                        flag = True
                    if not flag:
                        revert ("<ERROR>: Variable " + varName + " not an argument to the function", ctx)
                elif isMFI == "method":
                    for sym in self.symbols:
                        if sym.name == varName and sym.scope in [self.currentScope, "global"]:
                            flag = True
                            break
                    if not flag:
                        revert ("<ERROR>: Variable " + varName + " not declared", ctx)
        
                return self.getIdenType(c.iden().Iden().getText(), scope) # TODO: when writing to symbols, scope=contractType for contract variables

        # LPAREN expr RPAREN
        elif (c == ctx.LPAREN(0)):
            return self.exprType(ctx.getChild(1), scope)
        
        # expr DOT field=iden
        elif (ctx.DOT() and ctx.getChildCount() == 3):
            # enumStruct = ctx.expr(0).primitive().iden().Iden().getText()
            enumStruct = ctx.expr(0)
            valueField = ctx.field.Iden().getText()
            if enumStruct.primitive() and enumStruct.primitive().iden().Iden().getText() in self.enums:
                enumStruct = enumStruct.primitive().iden().Iden().getText() #self.exprType(enumStruct, self.currentScope, inFunctionCall)
                for sym in self.symbols:
                    if sym.name == valueField:
                        # if not sym.isEnumValue:
                        #     revert ("<ERROR>: " + valueField + " is not a valid enum value of type " + enumStruct)
                        if sym.type != enumStruct:
                            revert ("<ERROR>: '" + valueField + "' is not a valid enum value of type " + enumStruct)
                        return sym.type
            else:
                # lvalType = self.getIdenType(enumStruct, self.currentScope)
                lvalType = self.exprType(enumStruct, self.currentScope, inFunctionCall)
                if lvalType in self.structs: # Struct Field Access
                    rvalName = ctx.field.Iden().getText()
                    for sym in self.symbols:
                        if rvalName == sym.name:
                            if sym.fieldOfStruct != lvalType:
                                revert ("<ERROR>: '" + rvalName + "' is not a field of struct '" + lvalType + "'")
                            return self.getIdenType(rvalName, self.currentScope)
                
                elif lvalType in self.contracts: # InstMap Field Access
                    fieldName = ctx.field.Iden().getText()
                    for fields in self.fieldsOfContract[lvalType]:
                        if fieldName == fields[0]:
                            return fields[1]
                    revert ("<ERROR>: " + fieldName + " is not a field of contract " + lvalType, ctx.field)
                    
                # if lvalType not in self.structs:
                #     revert ("<ERROR>: '" + enumStruct.getText() + "' is not a valid struct object or enum type")


            # if enumStruct not in self.enums:
            #     revert ("<ERROR>: Enum not defined", ctx)
            # enumValue = ctx.field.Iden().getText()
            # for sym in self.symbols:
            #     if sym.name == enumValue:
            #         if sym.type != enumStruct:
            #             revert ("<ERROR>: Wrong enum")
            #         return sym.type

        # array=expr LBRACK index=expr RBRACK
        # array=expr LENGTH LPAREN RPAREN
        elif (ctx.array):
            arrayOrMap = self.exprType(ctx.getChild(0), scope)
            if self.isArray(arrayOrMap):
                arrayValueType = self.isArrayOf(arrayOrMap)
                if (ctx.LENGTH()):
                    return "uint"
                else:
                    if (ctx.index.MAPUPD()): # If the index inside the [] has a =>
                        updatedIndex = ctx.index.expr(0)
                        updatedIndexType = self.exprType(updatedIndex, scope)
                        # If the lhs of the => expression isn't an uint or a IntLiteral, revert
                        if (updatedIndexType != "uint" and not (updatedIndex.primitive() and updatedIndex.primitive().IntLiteral())):
                            revert ("<ERROR>: Array expects index of type uint", updatedIndex)
                        # else return the entire array type. Eg: array[address]
                        return arrayOrMap
                    # Standard array access: check if the index is of type uint or is an IntLiteral and revert if not
                    elif (self.exprType(ctx.index, scope) != "uint") and not (ctx.index.primitive() and ctx.index.primitive().IntLiteral()):
                        revert ("<ERROR>: Array expects index of type uint", ctx.index)
                        # if (ctx.index.MAPUPD()):
                        #     if (self.exprType(ctx.index.expr(0), scope) != arrayValueType):
                        #         if not (ctx.index.expr(0).primitive() and ctx.index.expr(0).primitive().IntLiteral() and arrayValueType in ["int", "uint"]):
                        #             revert ("<ERROR>: Array value type error", ctx.index.expr(0))
                        #     return arrayOrMap
                        # start = arrayOrMap.find("[") + 1
                        # end = arrayOrMap.find("]")
                        # arrayValueType = arrayOrMap[start:end]
                    # else return the value type of the array (array access)
                    return arrayValueType
                    # else:
                    #     print (self.exprType(ctx.index, scope))
                    #     revert ("<ERROR>: Array access expects index type uint", ctx)
            elif self.isMapping(arrayOrMap):
                if (ctx.LENGTH()):
                    revert (".length() is not defined for maps", ctx)
                else:
                    keyType = self.getMapKeyType(arrayOrMap)
                    valueType = self.getMapValueType(arrayOrMap)
                    if ctx.index.MAPUPD(): # TODO: Do this check separately
                        if (self.exprType(ctx.index.expr(0), scope) != keyType):
                            if not (ctx.index.expr(0).primitive() and ctx.index.expr(0).primitive().IntLiteral() and keyType in ["int", "uint"]):
                                revert ("<ERROR>: Key error", ctx.index.expr(0))
                        if (self.exprType(ctx.index.expr(1), scope) != valueType):
                            if not (ctx.index.expr(1).primitive() and ctx.index.expr(1).primitive().IntLiteral() and valueType in ["int", "uint"]):
                                revert ("<ERROR>: Value error", ctx)
                        return arrayOrMap
                    elif (self.exprType(ctx.index, scope) != keyType):
                        if not (ctx.index.primitive() and ctx.index.primitive().IntLiteral()) and keyType in ["int", "uint"]:
                            revert ("<ERROR>: Map access expects key type " + keyType, ctx)
                        return valueType
                    else:
                        return valueType
            elif arrayOrMap[0:8] == "inst_map":
                if (ctx.LENGTH()):
                    revert (".length() is not defined for inst_maps", ctx)
                if ctx.index.MAPUPD(): # TODO: Do this check separately
                    if (self.exprType(ctx.index.expr(0), scope) != "address"):
                        revert ("<ERROR>: Key error : inst_map expects key of type address", ctx)
                    if (self.exprType(ctx.index.expr(1), scope) != "bool"):
                        revert ("<ERROR>: Value error : inst_map expects value of type bool", ctx)
                    return arrayOrMap
                if (self.exprType(ctx.index, scope) != "address"):
                    revert ("<ERROR>: Key error : inst_map expects key of type address", ctx)
                return arrayOrMap[9:-1]
            else: # 5[1], etc
                revert ("<ERROR>: Not a valid map/array", ctx)
        
        # method=iden LPAREN rvalueList? RPAREN
        elif (ctx.method and not ctx.DOT()):
            methodName = ctx.method.Iden().getText()

            if methodName in self.contracts: # ContractName(values_of_fields)
                if self.getIsMethodFuncInv(self.currentScope) == "method":
                    revert ("<ERROR>: <contract_name>(...) can be used only in functions and invariants", ctx)
                contractFieldSignature = self.fieldsOfContract[methodName] # list of tuples (fieldName, fieldType)
                if ctx.rvalueList():
                    if len(contractFieldSignature) != len(ctx.rvalueList().rvalue()):
                        revert ("<ERROR>: Incorrect number of arguments passed", ctx)
                    i = 0
                    for param in ctx.rvalueList().rvalue(): # The order of args passed should be in the order of the fields declared
                        argType = self.exprType(param.expr(), scope, inFunctionCall=True)
                        supposedToBeType = contractFieldSignature[i][1]
                        if (argType != supposedToBeType):
                            if not (param.expr().primitive() and param.expr().primitive().IntLiteral() and supposedToBeType == "uint"):
                                revert ("<ERROR>: Field '" + contractFieldSignature[i][0] + "' is of type '" + supposedToBeType + "' not " + argType, ctx)
                        i = i + 1
                return methodName

            else: # Normal method/function calls
                methodReturnType = self.getIdenType(methodName, "global") # also checks whether variable has been declared or not
                i = 0
                symbol = None
                for sym in self.symbols:
                    if sym.name == methodName and (sym.scope == scope or sym.scope == "global"): # can remove the scope check, same otherwise
                        symbol = sym
                        break
                
                numberOfPassedArguments = 0
                if ctx.rvalueList():
                    numberOfPassedArguments = len(ctx.rvalueList().rvalue())
        
                if len(symbol.params) != numberOfPassedArguments:
                    revert ("<ERROR>: Method/function call arguments does not match function signature", ctx)

                if ctx.rvalueList():
                    for param in ctx.rvalueList().rvalue():
                        argType = self.exprType(param.expr(), scope, inFunctionCall=True)
                        if (argType != symbol.params[i]):
                            if not (param.expr().primitive() and param.expr().primitive().IntLiteral() and symbol.params[i] == "uint"):
                                revert ("<ERROR>: Method/function call arguments does not match function signature", ctx)
                        i = i + 1

                # Void functions cannot be used in expressions
                if methodReturnType == "void":
                    revert ("<ERROR>: Cannot use a void function in an expression", ctx)
                else:
                    return methodReturnType
        
        # op=(SUB | LNOT) expr
        elif (ctx.SUB() and (ctx.getChildCount() == 2)):
            if (self.exprType(ctx.expr(0), scope) not in ["int", "uint"]):
                revert ("<ERROR>: - used only for int/uint", ctx)
            else:
                return "int"
        
        # op=(SUB | LNOT) expr
        elif (ctx.LNOT() and (ctx.getChildCount() == 2)):
            if (self.exprType(ctx.expr(0), scope) != "bool"):
                revert ("<ERROR>: ! used only for bool", ctx)
            else:
                return "bool"

        # lhs=expr op=(ADD | SUB) rhs=expr
        # lhs=expr op=(MUL | DIV) rhs=expr
        elif (ctx.PLUS() or ctx.SUB() or ctx.MUL() or ctx.DIV() or ctx.MOD()
                or ctx.SAFEADD() or ctx.SAFESUB() or ctx.SAFEMUL() or ctx.SAFEDIV() or ctx.SAFEMOD()):
            op1 = ctx.lhs
            op2 = ctx.rhs
            if (op2.primitive() and op2.primitive().IntLiteral()):
                op1type = self.exprType(op1, scope)
                if op1type == "int":
                    return "int"
                elif op1type == "uint":
                    return "uint"
                else:
                    revert ("<ERROR>: Arithmetic operators are defined only on int and uint and both operands should be int or uint", ctx)
            elif (op1.primitive() and op1.primitive().IntLiteral()):
                op2type = self.exprType(op2, scope)
                if op2type == "int":
                    return "int"
                elif op2type == "uint":
                    return "uint"
                else:
                    revert ("<ERROR>: Arithmetic operators are defined only on int and uint and both operands should be int or uint", ctx)
            else:
                op1type = self.exprType(op1, scope)
                op2type = self.exprType(op2, scope)
                if (op1type == op2type and op1type == "int"):
                    return "int"
                elif (op1type == op2type and op1type == "uint"):
                    return "uint"
                else:
                    revert ("<ERROR>: Arithmetic operators are defined only on int and uint and both operands should be int or uint", ctx)
        
        # lhs=expr op=(LT | GT | GE | LE | IN) rhs=expr
        elif (ctx.LT() or ctx.GT() or ctx.GE() or ctx.LE()):
            op1 = ctx.getChild(0)
            op2 = ctx.getChild(2)
            if (op2.primitive() and op2.primitive().IntLiteral()):
                op1type = self.exprType(op1, scope)
                if op1type in ["int", "uint"]:
                    return "bool"
                else:
                    revert ("<ERROR>: Comparision operators are defined only on int and uint and both operands should be int or uint", ctx)
            elif (op1.primitive() and op1.primitive().IntLiteral()):
                op2type = self.exprType(op2, scope)
                if op2type in ["int", "uint"]:
                    return "bool"
                else:
                    revert ("<ERROR>: Comparision operators are defined only on int and uint and both operands should be int or uint", ctx)
            else:
                op1type = self.exprType(op1, scope)
                op2type = self.exprType(op2, scope)
                if (op1type == op2type and op1type in ["int", "uint"]):
                    return "bool"
                else:
                    revert ("<ERROR>: Comparision operators are defined only on int and uint and both operands should be int or uint", ctx)

        elif (ctx.IN()):
            isMFI = self.getIsMethodFuncInv(self.currentScope)
            # if isMFI == "method":
            #     print (ctx.getText())
            #     revert ("<ERROR>: 'in' cannot be used inside methods")
            op1 = ctx.getChild(0)
            op2 = ctx.getChild(2)
            op1Type = self.exprType(op1, scope)
            op2Type = self.exprType(op2, scope)
            if not self.isMapping(op2Type) and op2Type[0:8] != "inst_map" :
                revert ("<ERROR>: 'in' is defined only on maps", ctx)
            if self.isMapping(op2Type) and op1Type != self.getMapKeyType(op2Type): #TODO: Handle if keyType is int, then constant primitive etc
                revert ("<ERROR>: first operand of in has to be of keyType of map")
            elif op2Type[0:8] == "inst_map" and (op1Type != (op2Type[9:-1]) and op1Type != "address"):
                revert ("<ERROR>: first operand of in has to be of type " + op2Type[9:-1])
            return "bool"

        # lhs=expr op=(EQ | NE) rhs=expr
        elif (ctx.EQ() or ctx.NE()):
            op1 = ctx.getChild(0)
            op2 = ctx.getChild(2)
            op1type = self.exprType(op1, scope)
            op2type = self.exprType(op2, scope)
            if (op1type == op2type):
                if self.isSpec: # maps and arrays can be compared in spec
                    if self.isMapping(op1type):
                        self.FSTCodegen.temp = "mapping"
                    return "bool"
                elif self.isBaseType(op1type) or op1type in self.enums:
                    return "bool"
            elif (op1type != op2type) and ((op2type in ["int", "uint"] and op1.primitive() and op1.primitive().IntLiteral()) or (op1type in ["int", "uint"] and op2.primitive() and op2.primitive().IntLiteral())):
                return "bool"
            elif (op1type != op2type and ((op1type == "address" and op2type in self.contracts) or (op1type in self.contracts and op2type == "address"))): # can compare contract instances to addresses/null
                return "bool"
            else:
                revert ("<ERROR>: invalid use of == or != (operands have to be of same type)", ctx)

        # lhs=expr op=LAND rhs=expr
        # lhs=expr op=LOR rhs=expr
        elif (ctx.LAND() or ctx.LOR()):
            op1 = ctx.getChild(0)
            op2 = ctx.getChild(2)
            op1type = self.exprType(op1, scope)
            op2type = self.exprType(op2, scope)
            if not (op1type == "bool" and op2type == "bool"):
                revert ("<ERROR>: && and || are defined only on booleans", ctx)
            return "bool"

        # ITE
        elif (ctx.ITE()):
            isMFV = self.getIsMethodFuncInv(self.currentScope)
            if isMFV == "method" and not self.isSpec:
                revert ("<ERROR>: ite() cannot be used in methods", ctx)
            condition = ctx.condition
            thenBranch = ctx.thenBranch
            elseBranch = ctx.elseBranch
            conditionType = self.exprType(condition, scope)
            thenBranchType = self.exprType(thenBranch, scope)
            elseBranchType = self.exprType(elseBranch, scope)
            if (not (conditionType == "bool" and thenBranchType == "bool" and elseBranchType == "bool")):
                revert ("<ERROR>: All expressions in ite() should be of boolean type", ctx)
            return "bool"

        # TODO: complete for all comma separated exprs
        elif (ctx.MAPUPD()):
            if (self.getIsMethodFuncInv(self.currentScope) == "method" and not self.isSpec):
                revert ("<ERROR>: mapupd cannot be used inside methods", ctx)
            lhsType = self.exprType(ctx.expr(0), scope)
            rhsType = self.exprType(ctx.expr(1), scope) # Because this RHS is never typechecked otherwise
                                                        # Also check for the correct key value type
            # return "map[" + lhsType + "=>" + rhsType + "]"
            return rhsType

        elif (ctx.IMPL() or ctx.BIMPL()):
            if (self.getIsMethodFuncInv(self.currentScope) == "method" and not self.isSpec):
                revert ("<ERROR>: ==> cannot be used inside methods")
            expr1Type = self.exprType(ctx.expr(0), scope, inFunctionCall)
            expr2Type = self.exprType(ctx.expr(1), scope, inFunctionCall)
            if expr1Type != "bool" or expr2Type != "bool":
                revert ("<ERROR>: Both expressions for ==> have to be of type bool", ctx)
            return "bool"

        elif (ctx.FORALL() or ctx.EXISTS()):
            isMFI = self.getIsMethodFuncInv(self.currentScope)
            if isMFI == "method" and (not self.isSpec):
                revert ("<ERROR>: Cannot use " + ctx.getChild(0).getText() + " inside a method")
            for funParam in ctx.funParamList().funParam():
                paramName = funParam.name.Iden().getText()
                if self.checkIdentifierDeclared(paramName):
                    revert ("<ERROR>: Identifier " + paramName + " already used", funParam.name)
                paramType = funParam.datatype().getText()
                self.symbols.append(Symbol(_name=paramName, _type=paramType, _scope=self.currentScope, _isQuantifierVar=True))
            if self.exprType(ctx.expr(0), self.currentScope) != "bool":
                revert ("<ERROR>: forall/exists takes a boolean predicate as the second argument", ctx.expr(0))
            for param in ctx.funParamList().funParam():
                paramName = param.name.Iden().getText()
                paramType = param.datatype().getText()
                self.symbols.remove(Symbol(_name=paramName, _type=paramType, _scope=self.currentScope, _isQuantifierVar=True))
            return "bool"

        # iden LPAREN expr RPAREN                        //# CastExpr
        # NEW contractName=iden LPAREN rvalueList? RPAREN //# CreateExpr
        # TODO: Can be removed because there is a separate create statement
        elif (ctx.NEW() and ctx.getChildCount() == 5):
            newContractOf = ctx.contractName.Iden().getText()
            flag = True
            currentContractIndex = self.contracts.index(self.currentContract)
            for i in range (currentContractIndex):
                if self.contracts[i] == newContractOf:
                    flag = False
                    break
            if flag:
                revert ("<ERROR>: Contract " + newContractOf + " not found", ctx.contractName)
            return newContractOf

        elif (ctx.DEFAULT()):
            if (self.getIsMethodFuncInv(self.currentScope) == "method"):
                revert ("<ERROR>: 'default()' cannot be used inside methods", ctx)
            if (ctx.datatype().EVENTLOG() or ctx.datatype().EVENT()):
                revert ("<ERROR>: 'default()' for eventlog or event is undefined.", ctx.datatype())
            return ctx.datatype().getText()    

        # iden DOT ADD LPAREN NEW iden LPAREN rvalueList? RPAREN RPAREN SEMI //# InstMapAdd
        # Adding to a inst_map using new()
        elif (ctx.ADD()):
            instmapName = ctx.instmap.Iden().getText()
            instmapType = self.getIdenType(instmapName, scope)
            instmapOf = ""
            if instmapType[0:8] != "inst_map":
                revert ("<ERROR>: .add() is defined only for inst_maps", ctx.instmap)
            else:
                instmapOf = instmapType[9:instmapType.find(">")]

            newContractOf = ctx.contractName.Iden().getText()

            flag = True
            currentContractIndex = self.contracts.index(self.currentContract)
            for i in range (currentContractIndex):
                if self.contracts[i] == newContractOf:
                    flag = False
                    break
            if flag:
                revert ("<ERROR>: Contract " + newContractOf + " not found")

            if instmapOf != newContractOf:
                revert ("<ERROR>: contract created does not match type of instmap (" + instmapOf + ")", ctx.contractName)

            return newContractOf

        # TODO: Different contract method call
        # contractInstance=lvalue DOT method=iden LPAREN rvalueList? RPAREN

        elif (ctx.logcheck()):
            # for log in ctx.logcheck():
            if not ctx.logName.LOG():
                for symbol in self.symbols:
                    if symbol.name == ctx.logName.getText() and symbol.scope == scope:
                        if symbol.type != "eventlog":
                            revert ("<ERROR>: A value of type 'eventlog' has to follow ::", ctx.logName)
            # logType = self.exprType(logName, scope)
            #TODO: Check all logchecks
            # if logType != "eventlog":
            #     revert ("<ERROR>: " + logName + " has to be of type log", ctx.logName)
            return "eventlog"

        elif (ctx.PAYABLE()):
            if self.verificationMode == "VeriSol":
                revert ("<ERROR>: VeriSol doesn't support payable" + ctx.PAYABLE())     #Added for VeriSol compatibility.
            else:
                return self.exprType(ctx.expr(0), scope)

        elif (ctx.method and ctx.DOT()):
            if ctx.iden(0).Iden().getText() == "abi":
                if ctx.method.Iden().getText() in ["encode", "encodePacked", "encodeWithSelector", "encodeWithSignature"]:
                    return "bytes"

    def lvalueType(self, ctx:CelestialParser.LvalueContext, scope):
        """
        Similar to exprType but for the lvalue production rule to typecheck assignment, create and delete statements
        """

        if ctx.name:
            return self.getIdenType(ctx.iden().Iden().getText(), scope) # also checks whether variable declared or not

        elif ctx.LBRACK():
            arrayOrMap = self.lvalueType(ctx.getChild(0), scope)

            if self.isArray(arrayOrMap):
                if (ctx.expr()):
                    if (ctx.expr().primitive() and ctx.expr().primitive().IntLiteral()) or (self.exprType(ctx.expr(), scope) == "uint"):
                        start = arrayOrMap.find("[") + 1
                        end = arrayOrMap.rfind("]")
                        arrayValueType = arrayOrMap[start:end]
                        return arrayValueType
                    else:
                        revert ("<ERROR>: Array access expects index type uint", ctx)                        
            elif self.isMapping(arrayOrMap):
                keyType = self.getMapKeyType(arrayOrMap)
                if (self.exprType(ctx.expr(), scope) != keyType):
                    if not (ctx.expr().primitive() and ctx.expr().primitive().IntLiteral()) and keyType in ["int", "uint"]:
                        revert ("<ERROR>: Map access expects key type " + keyType, ctx)
                # else:
                valueType = self.getMapValueType(arrayOrMap)
                return valueType
            elif arrayOrMap[0:8] == "inst_map":
                if (self.exprType(ctx.expr(), scope) != "address"):
                    revert ("<ERROR>: Key error : inst_map expects key of type address", ctx)
                return arrayOrMap[9:-1]
            else: # 5[1], etc
                revert ("<ERROR>: Not a valid map/array", ctx)

        #TODO : lvalue DOT field=iden
        elif ctx.DOT() and ctx.getChildCount() == 3:
            lvalType = self.lvalueType(ctx.getChild(0), scope)
            if lvalType not in self.structs:
                revert ("<ERROR>: '" + lvalName + "' is not a valid struct object")
            lvalName = ctx.getChild(0).getText()
            rvalName = ctx.field.Iden().getText()

            for sym in self.symbols:
                if sym.name == rvalName and sym.fieldOfStruct != lvalType:
                    revert ("<ERROR>: Field '" + rvalName + "' does not belong to struct")

            return self.getIdenType(rvalName, scope)

    def enterStatement(self, ctx:CelestialParser.StatementContext):

        # Push Statement
        # name=expr DOT PUSH LPAREN value=expr RPAREN SEMI
        if ctx.PUSH():
            # if not (ctx.name.primitive() and ctx.name.primitive().iden()):
            #     revert ("<ERROR>: push() is defined only for arrays")
            # idenName = ctx.name.primitive().iden()
            idenType = self.lvalueType(ctx.arrayName, self.currentScope)
            if not self.isArray(idenType): # TODO: Correct this
                revert ("<ERROR>: push() is defined only for arrays", ctx)

            # Type of the value pushed to the array
            valueType = self.exprType(ctx.value, self.currentScope)

            # The actual type of the array
            arrayValueType = self.isArrayOf(idenType) # self.getArrayValueType(arrayName, self.currentScope)
            
            if ctx.arrayName.name: # Eg: variable.push()
                arrayName = ctx.arrayName.iden().Iden().getText()
            else: # Eg: variable[e].push()
                end = ctx.arrayName.getText().find("[")
                arrayName = ctx.arrayName.getText()[0:end]
            if valueType != arrayValueType:
                revert("<ERROR>: " + ctx.arrayName.getText() + ".push() expects a value of type " + arrayValueType, ctx)
            self.FSTCodegen.writePushStatement(ctx, self.symbols, self.currentScope, arrayName)
        
        elif ctx.POP():
            idenType = self.lvalueType(ctx.arrayName, self.currentScope)
            if not self.isArray(idenType): # TODO: Correct this
                revert ("<ERROR>: pop() is defined only for arrays", ctx)
            arrayName = ""
            if ctx.arrayName.name: # Eg: variable.pop()
                arrayName = ctx.arrayName.iden().Iden().getText()
            else: # Eg: variable[e].pop()
                end = ctx.arrayName.getText().find("[")
                arrayName = ctx.arrayName.getText()[0:end]
            self.FSTCodegen.writePopStatement(ctx, self.symbols, self.currentScope, arrayName)

        # Delete Statement
        elif ctx.DELETE():
            # TODO: DOT

            # Get the name of the variable
            if ctx.toDelete.name:
                varName = ctx.toDelete.iden().Iden().getText()
            else:
                end = ctx.toDelete.getText().find("[")
                varName = ctx.toDelete.getText()[0:end]
            
            # Check if that variable is a field
            if not self.FSTCodegen.existsField(varName):
                revert("<ERROR>: delete() works only for state variables", ctx)
            
            varType = self.lvalueType(ctx.toDelete, self.currentScope)
            # varType = self.getIdenType(varName, self.currentScope) wrong
            # If the variable is an array, typecheck its index if it has one
            if self.isArray(varType):
                if ctx.value: # It may not have an index (delete arrayName works)
                    deleteIndex = self.exprType(ctx.value, self.currentScope)
                    if deleteIndex != "uint" and not (ctx.value.primitive() and ctx.value.primitive().IntLiteral()):
                        revert("<ERROR>: Array index should be uint", ctx)
            # If the variable is a map, typecheck its key
            elif self.isMapping(varType):
                if not ctx.value:  # It is compulsory to have a key (delete mapName is wrong)
                    revert ("<ERROR>: delete() cannot be applied to mapping", ctx)
                else:
                    mapValueType = self.getMapValueType(varType)
                    if self.isMapping(mapValueType):                                # If it has a key but the value is also a map
                        revert ("<ERROR>: delete() cannot be applied to map", ctx)  # Example: the initial map was map[c => map[a => b]]
                    else: # Typecheck the map value now
                        deleteIndex = self.exprType(ctx.value, self.currentScope)
                        if deleteIndex != self.getMapKeyType(varType):
                            revert ("<ERROR>: map expects a key of type " + mapValueType, ctx)
            elif varType[0:8] == "inst_map":
                if not ctx.value:
                    revert ("<ERROR>: delete() cannot be applied to mapping (an index has to be provided)", ctx)
                else:
                    instMapType = varType[9:-1]
                    providedIndexType = self.exprType(ctx.value, self.currentScope)
                    if providedIndexType != instMapType and providedIndexType != "address":
                        revert ("<ERROR>: key has to be of type " + instMapType + " or address", ctx.value)
            # If the variable is neither an array nor map not instmap but has an index
            else:
                if ctx.value:
                    revert ("<ERROR>: Index provided for non-array/map type", ctx)

            self.FSTCodegen.writeDeleteStatement(ctx, self.symbols, self.currentScope)

        # Assert Statement
        # ASSERT expr (COMMA StringLiteral)? SEMI
        elif ctx.ASSERT():
            exprType = self.exprType(ctx.expr(0), self.currentScope)
            if exprType != "bool":
                revert("<ERROR>: assert() expects a boolean condition", ctx)
            self.FSTCodegen.writeAssertStatement(ctx, self.symbols, self.currentScope)
        
        # Create statement
        elif ctx.NEW() and ctx.ASSIGN():
            contractInstanceType = self.lvalueType(ctx.assignTo, self.currentScope)
            contractType = ctx.iden().Iden().getText()
            if contractInstanceType != contractType:
                revert ("<ERROR>: " + ctx.assignTo.getText() + " not of contract type " + contractType, ctx)
            self.FSTCodegen.writeNewStatement(ctx, self.symbols, self.currentScope)

        # Unkown call statements - 3 types possible
        #   a.call()
        #   bool s = a.call()
        #   s = a.call()
        elif ctx.CALL() or ctx.CALLUINT() or ctx.CALLBOOL():
            
            # Check if the '.call' is used on an 'address' type
            calleeType = self.exprType(ctx.expr(0), self.currentScope)
            if calleeType != "address":
                revert("<ERROR>: '.call()' can only be used on addresses")

            # If a local variable is declared
            if ctx.ASSIGN() and (ctx.BOOL() or ctx.UINT()):
                varName = ctx.iden().getText()

                # Check if the variable is redeclared
                for sym in self.symbols:
                    if (sym.name == varName and (sym.scope == self.currentScope or sym.scope == "global")):
                        revert ("<ERROR>: Variable '" + varName + "' redeclared", ctx.expr(0))

                # Add to the symbol table if it is not redeclared
                self.symbols.append(Symbol(_name=varName, _type=ctx.getChild(0).getText(), _scope=self.currentScope, _isParam=False, _isLocal=True))
                
            self.FSTCodegen.writeCallStatement(ctx, self.symbols, self.currentScope, self.reentrancyReverts)

        # Other contract method call statement
        elif ctx.otherContractInstance and not ctx.ASSIGN():
            methodCalled = ctx.method.Iden().getText()
            contractInstanceType = self.lvalueType(ctx.otherContractInstance, self.currentScope)

            # Checking if the method called is actually a method of the corresponding contract
            if methodCalled not in self.methodsOfContract[contractInstanceType].keys():
                revert ("<ERROR>: '" + methodCalled + "' not a method of contract " + contractInstanceType, ctx)

            # Checking if the arguments to the method match its signature
            methodArgumentTypes = self.methodsOfContract[contractInstanceType][methodCalled][1]
            if ctx.rvalueList():
                if len(methodArgumentTypes) != len(ctx.rvalueList().rvalue()):
                    revert ("<ERROR>: Method/function call arguments do not match function signature", ctx)
                i = 0
                for param in ctx.rvalueList().rvalue():
                    argType = self.exprType(param.expr(), self.currentScope)
                    if (argType != methodArgumentTypes[i]):
                        if not (param.expr().primitive() and param.expr().primitive().IntLiteral() and methodArgumentTypes[i] == "uint"):
                            revert ("<ERROR>: Method/function call arguments do not match function signature", ctx)
                    i = i + 1
            self.FSTCodegen.writeExternMethodCallStatement(ctx, self.symbols, self.currentScope)#, contractInstanceType)

        # Other contract method call and assigning return value
        elif ctx.otherContractInstance and ctx.ASSIGN():
            self.FSTCodegen.writeCtAssignmentStatement(ctx, self.symbols, self.currentScope)

        # Return Statement
        # RETURN expr? SEMI
        # elif ctx.RETURN():
        #     self.methodHasReturn = True
        #     currentFunctionReturnType = self.getIdenType(self.currentScope, self.currentScope)
        #     valueReturned = ctx.expr(0)
        #     if valueReturned:
        #         if currentFunctionReturnType == "void":
        #             revert ("<ERROR>: Return value should not be present")
        #         else:
        #             valueReturnedType = self.exprType(valueReturned, self.currentScope)
        #             if valueReturnedType != currentFunctionReturnType:
        #                 revert("<ERROR>: Invalid return type")
        #             else:
        #                 self.FSTCodegen.writeReturnStatement(ctx, self.symbols, self.currentScope, False)
        #     else:
        #         if currentFunctionReturnType != "void":
        #             revert ("<ERROR>: Expects a return value of type " + currentFunctionReturnType)
        #         else:
        #             self.FSTCodegen.writeReturnStatement(ctx, self.symbols, self.currentScope, True)

        # Assign Statement
        elif ctx.assignment:
            lhsType = self.lvalueType(ctx.assignTo, self.currentScope)
            rhsType = self.exprType(ctx.rvalue().expr(), self.currentScope)
            if lhsType != rhsType:
                if not (lhsType == "address" and rhsType in self.contracts) and not (lhsType == "uint" and ctx.rvalue().expr().primitive() and ctx.rvalue().expr().primitive().IntLiteral().getText()):
                    revert ("<ERROR>: Assignment statement type mismatch", ctx)
            self.FSTCodegen.writeAssignmentStatement(ctx, self.symbols, self.currentScope)

        # If Statement
        elif ctx.IF():
            conditionType = self.exprType(ctx.expr(0), self.currentScope)
            if conditionType != "bool":
                revert ("<ERROR>: If condition should be a boolean expression", ctx)
            self.FSTCodegen.writeIfStatement(ctx, self.symbols, self.currentScope)
            # if ctx.elseBranch:
                # self.FSTCodegen.writeStatement(ctx)

        # MethodCall Statement
        elif ctx.method and not ctx.DOT():
            methodName = ctx.iden().Iden().getText()
            methodReturnType = self.getIdenType(methodName, "global") # also checks whether method has been declared or not

            if self.getIsMethodFuncInv(methodName) in ["function", "invariant", "lemma"]:
                revert ("<ERROR>: Spec cannot be called from methods", ctx)

            i = 0
            symbol = None
            for sym in self.symbols:
                if sym.name == methodName and (sym.scope == self.currentScope or sym.scope == "global"): # can remove the scope check, same otherwise
                    symbol = sym
                    break

            numberOfPassedArguments = 0
            if ctx.rvalueList():
                numberOfPassedArguments = len(ctx.rvalueList().rvalue())
    
            if len(symbol.params) != numberOfPassedArguments:
                revert ("<ERROR>: Method/function call arguments do not match function signature", ctx)

            if ctx.rvalueList():
                for param in ctx.rvalueList().rvalue():
                    argType = self.exprType(param.expr(), self.currentScope)
                    if (argType != symbol.params[i]):
                        if not (param.expr().primitive() and param.expr().primitive().IntLiteral() and symbol.params[i] == "uint"):
                            revert ("<ERROR>: Method/function call arguments do not match function signature", ctx)
                    i = i + 1

            if methodReturnType == "void":
                self.FSTCodegen.writeMethodCallStatement(ctx, self.symbols, self.currentScope, True)
            else:
                self.FSTCodegen.writeMethodCallStatement(ctx, self.symbols, self.currentScope, False)

        # Revert Statement
        elif ctx.REVERT():
            self.FSTCodegen.writeRevertStatement(ctx)

        # Send Statement
        # elif ctx.SEND():
        #     payloadType = self.exprType(ctx.payload, self.currentScope, inFunctionCall=False)
        #     toType = self.exprType(ctx.contract, self.currentScope, inFunctionCall=False)
        #     if toType != "address":
        #         revert ("<ERROR>: First arg of send is address", ctx)

        #     if payloadType != "uint" and not (ctx.payload.primitive() and ctx.payload.primitive().IntLiteral()):
        #         revert ("<ERROR>: eTransfer send expects a uint", ctx)

        #     self.FSTCodegen.writeSendStatement(ctx, self.symbols, self.currentScope)

        elif ctx.TRANSFER():
            payloadType = self.exprType(ctx.amount, self.currentScope, inFunctionCall=False)
            toType = self.exprType(ctx.to, self.currentScope, inFunctionCall=False)
            if toType != "address":
                revert ("<ERROR>: 'transfer' is defined only on addresses", ctx.to)

            if payloadType != "uint" and not (ctx.amount.primitive() and ctx.amount.primitive().IntLiteral()):
                revert ("<ERROR>: 'transfer' expects a uint", ctx)

            self.FSTCodegen.writeTransferStatement(ctx, self.symbols, self.currentScope)
 

        elif ctx.EMIT():
            eventName = ctx.event.Iden().getText()
            requiredPayloadTypes = []
            for sym in self.symbols:
                if sym.name == eventName:
                    requiredPayloadTypes = sym.params
                    break
            
            numberOfProvidedArguments = len(ctx.expr())
            numberOfRequiredArguments = len(requiredPayloadTypes)
            if (numberOfProvidedArguments != numberOfRequiredArguments):
                revert ("<ERROR>: Got " + str(numberOfProvidedArguments) + " arguments, expected " + str(numberOfRequiredArguments) + " for event '" + eventName + "'", ctx.expr()[-1])

            for (payloadExpr, requiredPayloadType) in zip(ctx.expr(), requiredPayloadTypes):
                payloadType = self.exprType(payloadExpr, self.currentScope, inFunctionCall=False)
                if payloadType != requiredPayloadType:
                    if (payloadExpr.primitive() and payloadExpr.primitive().IntLiteral()):
                        if requiredPayloadType != "uint":
                            revert ("<ERROR>: Invalid payload type", payloadExpr)
            self.FSTCodegen.writeEmitStatement(ctx, self.symbols, self.currentScope)

        self.SolidityCodegen.writeStatement(ctx, self.symbols, self.currentScope)

    def exitStatement(self, ctx:CelestialParser.StatementContext):
        self.SolidityCodegen.exitWriteStatement(ctx)
        if ctx.IF() and not ctx.elseStatement():
            self.FSTCodegen.endIfWithoutElse(self.currentScope)

    def enterElseStatement(self, ctx:CelestialParser.ElseStatementContext):
        self.FSTCodegen.writeElseStatement(ctx, self.currentScope)
        self.SolidityCodegen.writeElseStatement(ctx)
    
    def exitElseStatement(self, ctx:CelestialParser.ElseStatementContext):
        self.FSTCodegen.endElse(ctx, self.currentScope)

    def checkIdentifierDeclared(self, identifier, scope=""):
        if scope != "":
            for symbol in self.symbols:
                if symbol.name == identifier and symbol.scope == scope:
                    return True
            return False
        else:
            for symbol in self.symbols:
                if symbol.name == identifier:
                    return True
            return False

    def getArrayValueType(self, identifier, scope):
        """
        Given an array type, returns what it is an array of
            Example: array[map[int => int]] returns map[int => int]
        """

        arrayType = self.getIdenType(identifier, scope)
        arrayValueType = self.isArrayOf(arrayType)
        return arrayValueType

    # def getMapValueType(self, identifier, scope):
    #     """
    #     Given a map type, returns the valuetype of the map
    #     """

    #     mapType = self.getIdenType(identifier, scope)
    #     mapValueType = mapType[mapType.find("=>") + 2:mapType.rfind("]")]
    #     return mapValueType

    def getIdenType(self, identifier, scope):
        """
        Given an identifier, returns the type of the identifier after looking up the symbol table
        The type is a string just as in `exprType`
        Error if the identifier is not found (because `getIdenType` is called only when the identifier is 
            accessed, and hence can throw an error)
        """

        if identifier == "eTransfer":
            return ""
        elif identifier == "constructor":
            return "void"
        elif identifier in self.enums:
            return identifier
        elif identifier in ["fallback", "receive"]:
            return "void" 
        for sym in self.symbols:
            if sym.name == identifier and (sym.scope == scope or sym.scope == "global"):
                return sym.type
        revert ("<ERROR>: Variable " + identifier + " not found")

    def getSym(self, identifier):
        for sym in self.symbols:
            if sym.name == identifier:
                return sym

    def printSymbolTable (self):
        """
        Prints the symbol table if the --printSymbolTable option is used
        """

        t = PrettyTable(['Name', 'Type', 'Parameters', 'Scope', 'isParameter?', 'isMethod?', 'isMap?', 'mapKeyType'])
        for sym in self.symbols:
            t.add_row([sym.name, sym.type, sym.params, sym.scope, sym.isParam, sym.isMethod, sym.isMap, sym.mapKeyType])
        print (t)

    def getIsMethodFuncInv (self, name):
        """
        Given an identifier, returns whether it is a function, invariant or method
        """
        
        if name == ["constructor", "receive", "fallback"]:
            return "method"
        for sym in self.symbols:
            if name == sym.name and sym.scope == "global": # scope = global not necessary
                if sym.isMethod:
                    return "method"
                elif sym.isFunction:
                    return "function"
                elif sym.isInvariant:
                    return "invariant"
                break
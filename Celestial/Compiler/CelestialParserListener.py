# Generated from CelestialParser.g4 by ANTLR 4.7.1
from antlr4 import *
if __name__ is not None and "." in __name__:
    from .CelestialParser import CelestialParser
else:
    from CelestialParser import CelestialParser

# This class defines a complete listener for a parse tree produced by CelestialParser.
class CelestialParserListener(ParseTreeListener):

    # Enter a parse tree produced by CelestialParser#program.
    def enterProgram(self, ctx:CelestialParser.ProgramContext):
        pass

    # Exit a parse tree produced by CelestialParser#program.
    def exitProgram(self, ctx:CelestialParser.ProgramContext):
        pass


    # Enter a parse tree produced by CelestialParser#pragmaDirective.
    def enterPragmaDirective(self, ctx:CelestialParser.PragmaDirectiveContext):
        pass

    # Exit a parse tree produced by CelestialParser#pragmaDirective.
    def exitPragmaDirective(self, ctx:CelestialParser.PragmaDirectiveContext):
        pass


    # Enter a parse tree produced by CelestialParser#pragmaValue.
    def enterPragmaValue(self, ctx:CelestialParser.PragmaValueContext):
        pass

    # Exit a parse tree produced by CelestialParser#pragmaValue.
    def exitPragmaValue(self, ctx:CelestialParser.PragmaValueContext):
        pass


    # Enter a parse tree produced by CelestialParser#version.
    def enterVersion(self, ctx:CelestialParser.VersionContext):
        pass

    # Exit a parse tree produced by CelestialParser#version.
    def exitVersion(self, ctx:CelestialParser.VersionContext):
        pass


    # Enter a parse tree produced by CelestialParser#versionConstraint.
    def enterVersionConstraint(self, ctx:CelestialParser.VersionConstraintContext):
        pass

    # Exit a parse tree produced by CelestialParser#versionConstraint.
    def exitVersionConstraint(self, ctx:CelestialParser.VersionConstraintContext):
        pass


    # Enter a parse tree produced by CelestialParser#versionOperator.
    def enterVersionOperator(self, ctx:CelestialParser.VersionOperatorContext):
        pass

    # Exit a parse tree produced by CelestialParser#versionOperator.
    def exitVersionOperator(self, ctx:CelestialParser.VersionOperatorContext):
        pass


    # Enter a parse tree produced by CelestialParser#importDirective.
    def enterImportDirective(self, ctx:CelestialParser.ImportDirectiveContext):
        pass

    # Exit a parse tree produced by CelestialParser#importDirective.
    def exitImportDirective(self, ctx:CelestialParser.ImportDirectiveContext):
        pass


    # Enter a parse tree produced by CelestialParser#importDeclaration.
    def enterImportDeclaration(self, ctx:CelestialParser.ImportDeclarationContext):
        pass

    # Exit a parse tree produced by CelestialParser#importDeclaration.
    def exitImportDeclaration(self, ctx:CelestialParser.ImportDeclarationContext):
        pass


    # Enter a parse tree produced by CelestialParser#iden.
    def enterIden(self, ctx:CelestialParser.IdenContext):
        pass

    # Exit a parse tree produced by CelestialParser#iden.
    def exitIden(self, ctx:CelestialParser.IdenContext):
        pass


    # Enter a parse tree produced by CelestialParser#datatype.
    def enterDatatype(self, ctx:CelestialParser.DatatypeContext):
        pass

    # Exit a parse tree produced by CelestialParser#datatype.
    def exitDatatype(self, ctx:CelestialParser.DatatypeContext):
        pass


    # Enter a parse tree produced by CelestialParser#idenTypeList.
    def enterIdenTypeList(self, ctx:CelestialParser.IdenTypeListContext):
        pass

    # Exit a parse tree produced by CelestialParser#idenTypeList.
    def exitIdenTypeList(self, ctx:CelestialParser.IdenTypeListContext):
        pass


    # Enter a parse tree produced by CelestialParser#idenType.
    def enterIdenType(self, ctx:CelestialParser.IdenTypeContext):
        pass

    # Exit a parse tree produced by CelestialParser#idenType.
    def exitIdenType(self, ctx:CelestialParser.IdenTypeContext):
        pass


    # Enter a parse tree produced by CelestialParser#contractDecl.
    def enterContractDecl(self, ctx:CelestialParser.ContractDeclContext):
        pass

    # Exit a parse tree produced by CelestialParser#contractDecl.
    def exitContractDecl(self, ctx:CelestialParser.ContractDeclContext):
        pass


    # Enter a parse tree produced by CelestialParser#contractBody.
    def enterContractBody(self, ctx:CelestialParser.ContractBodyContext):
        pass

    # Exit a parse tree produced by CelestialParser#contractBody.
    def exitContractBody(self, ctx:CelestialParser.ContractBodyContext):
        pass


    # Enter a parse tree produced by CelestialParser#contractContents.
    def enterContractContents(self, ctx:CelestialParser.ContractContentsContext):
        pass

    # Exit a parse tree produced by CelestialParser#contractContents.
    def exitContractContents(self, ctx:CelestialParser.ContractContentsContext):
        pass


    # Enter a parse tree produced by CelestialParser#enumDecl.
    def enterEnumDecl(self, ctx:CelestialParser.EnumDeclContext):
        pass

    # Exit a parse tree produced by CelestialParser#enumDecl.
    def exitEnumDecl(self, ctx:CelestialParser.EnumDeclContext):
        pass


    # Enter a parse tree produced by CelestialParser#structDecl.
    def enterStructDecl(self, ctx:CelestialParser.StructDeclContext):
        pass

    # Exit a parse tree produced by CelestialParser#structDecl.
    def exitStructDecl(self, ctx:CelestialParser.StructDeclContext):
        pass


    # Enter a parse tree produced by CelestialParser#FDecl.
    def enterFDecl(self, ctx:CelestialParser.FDeclContext):
        pass

    # Exit a parse tree produced by CelestialParser#FDecl.
    def exitFDecl(self, ctx:CelestialParser.FDeclContext):
        pass


    # Enter a parse tree produced by CelestialParser#funParamList.
    def enterFunParamList(self, ctx:CelestialParser.FunParamListContext):
        pass

    # Exit a parse tree produced by CelestialParser#funParamList.
    def exitFunParamList(self, ctx:CelestialParser.FunParamListContext):
        pass


    # Enter a parse tree produced by CelestialParser#funParam.
    def enterFunParam(self, ctx:CelestialParser.FunParamContext):
        pass

    # Exit a parse tree produced by CelestialParser#funParam.
    def exitFunParam(self, ctx:CelestialParser.FunParamContext):
        pass


    # Enter a parse tree produced by CelestialParser#functionBody.
    def enterFunctionBody(self, ctx:CelestialParser.FunctionBodyContext):
        pass

    # Exit a parse tree produced by CelestialParser#functionBody.
    def exitFunctionBody(self, ctx:CelestialParser.FunctionBodyContext):
        pass


    # Enter a parse tree produced by CelestialParser#invariantDecl.
    def enterInvariantDecl(self, ctx:CelestialParser.InvariantDeclContext):
        pass

    # Exit a parse tree produced by CelestialParser#invariantDecl.
    def exitInvariantDecl(self, ctx:CelestialParser.InvariantDeclContext):
        pass


    # Enter a parse tree produced by CelestialParser#invariantBody.
    def enterInvariantBody(self, ctx:CelestialParser.InvariantBodyContext):
        pass

    # Exit a parse tree produced by CelestialParser#invariantBody.
    def exitInvariantBody(self, ctx:CelestialParser.InvariantBodyContext):
        pass


    # Enter a parse tree produced by CelestialParser#eventDecl.
    def enterEventDecl(self, ctx:CelestialParser.EventDeclContext):
        pass

    # Exit a parse tree produced by CelestialParser#eventDecl.
    def exitEventDecl(self, ctx:CelestialParser.EventDeclContext):
        pass


    # Enter a parse tree produced by CelestialParser#constructorDecl.
    def enterConstructorDecl(self, ctx:CelestialParser.ConstructorDeclContext):
        pass

    # Exit a parse tree produced by CelestialParser#constructorDecl.
    def exitConstructorDecl(self, ctx:CelestialParser.ConstructorDeclContext):
        pass


    # Enter a parse tree produced by CelestialParser#spec.
    def enterSpec(self, ctx:CelestialParser.SpecContext):
        pass

    # Exit a parse tree produced by CelestialParser#spec.
    def exitSpec(self, ctx:CelestialParser.SpecContext):
        pass


    # Enter a parse tree produced by CelestialParser#stateMutability.
    def enterStateMutability(self, ctx:CelestialParser.StateMutabilityContext):
        pass

    # Exit a parse tree produced by CelestialParser#stateMutability.
    def exitStateMutability(self, ctx:CelestialParser.StateMutabilityContext):
        pass


    # Enter a parse tree produced by CelestialParser#MDecl.
    def enterMDecl(self, ctx:CelestialParser.MDeclContext):
        pass

    # Exit a parse tree produced by CelestialParser#MDecl.
    def exitMDecl(self, ctx:CelestialParser.MDeclContext):
        pass


    # Enter a parse tree produced by CelestialParser#methodParamList.
    def enterMethodParamList(self, ctx:CelestialParser.MethodParamListContext):
        pass

    # Exit a parse tree produced by CelestialParser#methodParamList.
    def exitMethodParamList(self, ctx:CelestialParser.MethodParamListContext):
        pass


    # Enter a parse tree produced by CelestialParser#methodParam.
    def enterMethodParam(self, ctx:CelestialParser.MethodParamContext):
        pass

    # Exit a parse tree produced by CelestialParser#methodParam.
    def exitMethodParam(self, ctx:CelestialParser.MethodParamContext):
        pass


    # Enter a parse tree produced by CelestialParser#methodBody.
    def enterMethodBody(self, ctx:CelestialParser.MethodBodyContext):
        pass

    # Exit a parse tree produced by CelestialParser#methodBody.
    def exitMethodBody(self, ctx:CelestialParser.MethodBodyContext):
        pass


    # Enter a parse tree produced by CelestialParser#returnStatement.
    def enterReturnStatement(self, ctx:CelestialParser.ReturnStatementContext):
        pass

    # Exit a parse tree produced by CelestialParser#returnStatement.
    def exitReturnStatement(self, ctx:CelestialParser.ReturnStatementContext):
        pass


    # Enter a parse tree produced by CelestialParser#varDecl.
    def enterVarDecl(self, ctx:CelestialParser.VarDeclContext):
        pass

    # Exit a parse tree produced by CelestialParser#varDecl.
    def exitVarDecl(self, ctx:CelestialParser.VarDeclContext):
        pass


    # Enter a parse tree produced by CelestialParser#usingForDecl.
    def enterUsingForDecl(self, ctx:CelestialParser.UsingForDeclContext):
        pass

    # Exit a parse tree produced by CelestialParser#usingForDecl.
    def exitUsingForDecl(self, ctx:CelestialParser.UsingForDeclContext):
        pass


    # Enter a parse tree produced by CelestialParser#loopVarDecl.
    def enterLoopVarDecl(self, ctx:CelestialParser.LoopVarDeclContext):
        pass

    # Exit a parse tree produced by CelestialParser#loopVarDecl.
    def exitLoopVarDecl(self, ctx:CelestialParser.LoopVarDeclContext):
        pass


    # Enter a parse tree produced by CelestialParser#statement.
    def enterStatement(self, ctx:CelestialParser.StatementContext):
        pass

    # Exit a parse tree produced by CelestialParser#statement.
    def exitStatement(self, ctx:CelestialParser.StatementContext):
        pass


    # Enter a parse tree produced by CelestialParser#elseStatement.
    def enterElseStatement(self, ctx:CelestialParser.ElseStatementContext):
        pass

    # Exit a parse tree produced by CelestialParser#elseStatement.
    def exitElseStatement(self, ctx:CelestialParser.ElseStatementContext):
        pass


    # Enter a parse tree produced by CelestialParser#lvalue.
    def enterLvalue(self, ctx:CelestialParser.LvalueContext):
        pass

    # Exit a parse tree produced by CelestialParser#lvalue.
    def exitLvalue(self, ctx:CelestialParser.LvalueContext):
        pass


    # Enter a parse tree produced by CelestialParser#logcheck.
    def enterLogcheck(self, ctx:CelestialParser.LogcheckContext):
        pass

    # Exit a parse tree produced by CelestialParser#logcheck.
    def exitLogcheck(self, ctx:CelestialParser.LogcheckContext):
        pass


    # Enter a parse tree produced by CelestialParser#expr.
    def enterExpr(self, ctx:CelestialParser.ExprContext):
        pass

    # Exit a parse tree produced by CelestialParser#expr.
    def exitExpr(self, ctx:CelestialParser.ExprContext):
        pass


    # Enter a parse tree produced by CelestialParser#primitive.
    def enterPrimitive(self, ctx:CelestialParser.PrimitiveContext):
        pass

    # Exit a parse tree produced by CelestialParser#primitive.
    def exitPrimitive(self, ctx:CelestialParser.PrimitiveContext):
        pass


    # Enter a parse tree produced by CelestialParser#unnamedTupleBody.
    def enterUnnamedTupleBody(self, ctx:CelestialParser.UnnamedTupleBodyContext):
        pass

    # Exit a parse tree produced by CelestialParser#unnamedTupleBody.
    def exitUnnamedTupleBody(self, ctx:CelestialParser.UnnamedTupleBodyContext):
        pass


    # Enter a parse tree produced by CelestialParser#namedTupleBody.
    def enterNamedTupleBody(self, ctx:CelestialParser.NamedTupleBodyContext):
        pass

    # Exit a parse tree produced by CelestialParser#namedTupleBody.
    def exitNamedTupleBody(self, ctx:CelestialParser.NamedTupleBodyContext):
        pass


    # Enter a parse tree produced by CelestialParser#rvalueList.
    def enterRvalueList(self, ctx:CelestialParser.RvalueListContext):
        pass

    # Exit a parse tree produced by CelestialParser#rvalueList.
    def exitRvalueList(self, ctx:CelestialParser.RvalueListContext):
        pass


    # Enter a parse tree produced by CelestialParser#rvalue.
    def enterRvalue(self, ctx:CelestialParser.RvalueContext):
        pass

    # Exit a parse tree produced by CelestialParser#rvalue.
    def exitRvalue(self, ctx:CelestialParser.RvalueContext):
        pass



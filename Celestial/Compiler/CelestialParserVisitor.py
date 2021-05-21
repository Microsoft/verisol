# Generated from CelestialParser.g4 by ANTLR 4.7.1
from antlr4 import *
if __name__ is not None and "." in __name__:
    from .CelestialParser import CelestialParser
else:
    from CelestialParser import CelestialParser

# This class defines a complete generic visitor for a parse tree produced by CelestialParser.

class CelestialParserVisitor(ParseTreeVisitor):

    # Visit a parse tree produced by CelestialParser#program.
    def visitProgram(self, ctx:CelestialParser.ProgramContext):
        return self.visitChildren(ctx)


    # Visit a parse tree produced by CelestialParser#pragmaDirective.
    def visitPragmaDirective(self, ctx:CelestialParser.PragmaDirectiveContext):
        return self.visitChildren(ctx)


    # Visit a parse tree produced by CelestialParser#pragmaValue.
    def visitPragmaValue(self, ctx:CelestialParser.PragmaValueContext):
        return self.visitChildren(ctx)


    # Visit a parse tree produced by CelestialParser#version.
    def visitVersion(self, ctx:CelestialParser.VersionContext):
        return self.visitChildren(ctx)


    # Visit a parse tree produced by CelestialParser#versionConstraint.
    def visitVersionConstraint(self, ctx:CelestialParser.VersionConstraintContext):
        return self.visitChildren(ctx)


    # Visit a parse tree produced by CelestialParser#versionOperator.
    def visitVersionOperator(self, ctx:CelestialParser.VersionOperatorContext):
        return self.visitChildren(ctx)


    # Visit a parse tree produced by CelestialParser#importDirective.
    def visitImportDirective(self, ctx:CelestialParser.ImportDirectiveContext):
        return self.visitChildren(ctx)


    # Visit a parse tree produced by CelestialParser#importDeclaration.
    def visitImportDeclaration(self, ctx:CelestialParser.ImportDeclarationContext):
        return self.visitChildren(ctx)


    # Visit a parse tree produced by CelestialParser#iden.
    def visitIden(self, ctx:CelestialParser.IdenContext):
        return self.visitChildren(ctx)


    # Visit a parse tree produced by CelestialParser#datatype.
    def visitDatatype(self, ctx:CelestialParser.DatatypeContext):
        return self.visitChildren(ctx)


    # Visit a parse tree produced by CelestialParser#idenTypeList.
    def visitIdenTypeList(self, ctx:CelestialParser.IdenTypeListContext):
        return self.visitChildren(ctx)


    # Visit a parse tree produced by CelestialParser#idenType.
    def visitIdenType(self, ctx:CelestialParser.IdenTypeContext):
        return self.visitChildren(ctx)


    # Visit a parse tree produced by CelestialParser#contractDecl.
    def visitContractDecl(self, ctx:CelestialParser.ContractDeclContext):
        return self.visitChildren(ctx)


    # Visit a parse tree produced by CelestialParser#contractBody.
    def visitContractBody(self, ctx:CelestialParser.ContractBodyContext):
        return self.visitChildren(ctx)


    # Visit a parse tree produced by CelestialParser#contractContents.
    def visitContractContents(self, ctx:CelestialParser.ContractContentsContext):
        return self.visitChildren(ctx)


    # Visit a parse tree produced by CelestialParser#enumDecl.
    def visitEnumDecl(self, ctx:CelestialParser.EnumDeclContext):
        return self.visitChildren(ctx)


    # Visit a parse tree produced by CelestialParser#structDecl.
    def visitStructDecl(self, ctx:CelestialParser.StructDeclContext):
        return self.visitChildren(ctx)


    # Visit a parse tree produced by CelestialParser#FDecl.
    def visitFDecl(self, ctx:CelestialParser.FDeclContext):
        return self.visitChildren(ctx)


    # Visit a parse tree produced by CelestialParser#funParamList.
    def visitFunParamList(self, ctx:CelestialParser.FunParamListContext):
        return self.visitChildren(ctx)


    # Visit a parse tree produced by CelestialParser#funParam.
    def visitFunParam(self, ctx:CelestialParser.FunParamContext):
        return self.visitChildren(ctx)


    # Visit a parse tree produced by CelestialParser#functionBody.
    def visitFunctionBody(self, ctx:CelestialParser.FunctionBodyContext):
        return self.visitChildren(ctx)


    # Visit a parse tree produced by CelestialParser#invariantDecl.
    def visitInvariantDecl(self, ctx:CelestialParser.InvariantDeclContext):
        return self.visitChildren(ctx)


    # Visit a parse tree produced by CelestialParser#invariantBody.
    def visitInvariantBody(self, ctx:CelestialParser.InvariantBodyContext):
        return self.visitChildren(ctx)


    # Visit a parse tree produced by CelestialParser#eventDecl.
    def visitEventDecl(self, ctx:CelestialParser.EventDeclContext):
        return self.visitChildren(ctx)


    # Visit a parse tree produced by CelestialParser#constructorDecl.
    def visitConstructorDecl(self, ctx:CelestialParser.ConstructorDeclContext):
        return self.visitChildren(ctx)


    # Visit a parse tree produced by CelestialParser#spec.
    def visitSpec(self, ctx:CelestialParser.SpecContext):
        return self.visitChildren(ctx)


    # Visit a parse tree produced by CelestialParser#stateMutability.
    def visitStateMutability(self, ctx:CelestialParser.StateMutabilityContext):
        return self.visitChildren(ctx)


    # Visit a parse tree produced by CelestialParser#MDecl.
    def visitMDecl(self, ctx:CelestialParser.MDeclContext):
        return self.visitChildren(ctx)


    # Visit a parse tree produced by CelestialParser#methodParamList.
    def visitMethodParamList(self, ctx:CelestialParser.MethodParamListContext):
        return self.visitChildren(ctx)


    # Visit a parse tree produced by CelestialParser#methodParam.
    def visitMethodParam(self, ctx:CelestialParser.MethodParamContext):
        return self.visitChildren(ctx)


    # Visit a parse tree produced by CelestialParser#methodBody.
    def visitMethodBody(self, ctx:CelestialParser.MethodBodyContext):
        return self.visitChildren(ctx)


    # Visit a parse tree produced by CelestialParser#returnStatement.
    def visitReturnStatement(self, ctx:CelestialParser.ReturnStatementContext):
        return self.visitChildren(ctx)


    # Visit a parse tree produced by CelestialParser#varDecl.
    def visitVarDecl(self, ctx:CelestialParser.VarDeclContext):
        return self.visitChildren(ctx)


    # Visit a parse tree produced by CelestialParser#usingForDecl.
    def visitUsingForDecl(self, ctx:CelestialParser.UsingForDeclContext):
        return self.visitChildren(ctx)


    # Visit a parse tree produced by CelestialParser#loopVarDecl.
    def visitLoopVarDecl(self, ctx:CelestialParser.LoopVarDeclContext):
        return self.visitChildren(ctx)


    # Visit a parse tree produced by CelestialParser#statement.
    def visitStatement(self, ctx:CelestialParser.StatementContext):
        return self.visitChildren(ctx)


    # Visit a parse tree produced by CelestialParser#elseStatement.
    def visitElseStatement(self, ctx:CelestialParser.ElseStatementContext):
        return self.visitChildren(ctx)


    # Visit a parse tree produced by CelestialParser#lvalue.
    def visitLvalue(self, ctx:CelestialParser.LvalueContext):
        return self.visitChildren(ctx)


    # Visit a parse tree produced by CelestialParser#logcheck.
    def visitLogcheck(self, ctx:CelestialParser.LogcheckContext):
        return self.visitChildren(ctx)


    # Visit a parse tree produced by CelestialParser#expr.
    def visitExpr(self, ctx:CelestialParser.ExprContext):
        return self.visitChildren(ctx)


    # Visit a parse tree produced by CelestialParser#primitive.
    def visitPrimitive(self, ctx:CelestialParser.PrimitiveContext):
        return self.visitChildren(ctx)


    # Visit a parse tree produced by CelestialParser#unnamedTupleBody.
    def visitUnnamedTupleBody(self, ctx:CelestialParser.UnnamedTupleBodyContext):
        return self.visitChildren(ctx)


    # Visit a parse tree produced by CelestialParser#namedTupleBody.
    def visitNamedTupleBody(self, ctx:CelestialParser.NamedTupleBodyContext):
        return self.visitChildren(ctx)


    # Visit a parse tree produced by CelestialParser#rvalueList.
    def visitRvalueList(self, ctx:CelestialParser.RvalueListContext):
        return self.visitChildren(ctx)


    # Visit a parse tree produced by CelestialParser#rvalue.
    def visitRvalue(self, ctx:CelestialParser.RvalueContext):
        return self.visitChildren(ctx)



del CelestialParser
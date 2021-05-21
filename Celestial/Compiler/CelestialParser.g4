parser grammar CelestialParser;

options {tokenVocab = CelestialLexer; language=Python3;}// CelestialLexer;

// A small overview of ANTLRs parser rules:
//
// Parser rules begin with a lower case letter, lexer rules begin 
// with an Uppercase letter. To create a parser rule, write the name
// followed by a colon (:) and then a list of alternatives, separated
// by pipe (|) characters. You can use parenthesis for sub-expressions,
// alternatives within those sub-expressions, and kleene * or + on any
// element in a rule.
//
// Every production rule corresponds to a class that gets generated
// in the target language for the ANTLR generator. If we use alternative
// labels, as in `type`, then subclasses of the rule-class will be created
// for each label. If one alternative is labelled, then they all must be.
// The purpose of labels is to call different functions in the generated
// listeners and visitors for the results of these productions.
//
// Lastly, ANTLR's DSL contains a feature that allows us to name the matched
// tokens and productions in an alternative (name=part) or collect multiple
// tokens or productions of the same type into a list (list+=part). The `type`
// production below uses this feature, too.

program : (contractDecl | pragmaDirective | importDirective)* EOF ;

pragmaDirective
  : PRAGMA pragmaName=iden pragmaValue SEMI ;

pragmaValue
  : version | expr ;

version
  : versionConstraint versionConstraint? ;

versionConstraint
  : versionOperator? VersionLiteral ;

versionOperator
  : CARET | BNOT | GE | GT | LT | LE | ASSIGN ;

importDirective
  : IMPORT StringLiteral+ (AS iden)? SEMI
  | IMPORT (MUL | iden) (AS iden)? FROM StringLiteral+ SEMI
  | IMPORT LBRACE importDeclaration ( COMMA importDeclaration )* RBRACE FROM StringLiteral+ SEMI ;

importDeclaration : iden (AS iden)? ;

iden : Iden ;
//int  : IntLiteral ;

datatype : arrayType=datatype LBRACK RBRACK
         | MAP LPAREN keyType=datatype MAPUPD valueType=datatype RPAREN
         | BOOL
         | INT
         | UINT
         | UINT8
         | STRING
         | ADDR (PAYABLE)?
         | EVENTLOG
         | EVENT
         | name=iden
         | INSTMAP LT iden GT
         | BYTES
         | BYTES20
         | BYTES32
         ;

idenTypeList : idenType (COMMA idenType)* ;
idenType : name=iden COLON datatype ;

contractDecl : CONTRACT name=iden contractBody  ;

contractBody : LBRACE contractContents+ RBRACE ;

contractContents : varDecl
                 | enumDecl
                 | structDecl
                 | funDecl
                 | invariantDecl
                 | eventDecl
                 | constructorDecl
                 | methodDecl
                 | usingForDecl
                 ;

enumDecl : ENUM name=iden LBRACE iden (COMMA iden)* RBRACE ;

structDecl : STRUCT name=iden LBRACE datatype iden SEMI (datatype iden SEMI)* RBRACE ;

funDecl : SPEC name=iden LPAREN funParamList? RPAREN functionBody # FDecl
        ;

funParamList : funParam (COMMA funParam)* ;
funParam : datatype name=iden ;
functionBody : LBRACE expr RBRACE ;

invariantDecl : INVARIANT name=iden invariantBody ;
invariantBody : LBRACE expr RBRACE ;

eventDecl : EVENT name=iden LPAREN (datatype (COMMA datatype)*)? RPAREN SEMI ;

constructorDecl : CONSTR LPAREN methodParamList? RPAREN (PUBLIC|PRIVATE)? spec (MODIFIES LBRACK (modifies=rvalueList)? RBRACK)? (MODIFIESA LBRACK (modifies_addrs=rvalueList)? RBRACK)? methodBody ;

spec : (PRE pre=expr)? (POST post=expr)? (CREDIT)? (DEBIT)? (TXREVERTS reverts=expr)? (RREVERTS rreverts=expr)?
     | (CREDIT)? (DEBIT)? (PRE pre=expr)? (POST post=expr)? (TXREVERTS reverts=expr)? (RREVERTS rreverts=expr)?
     | (PRE pre=expr)? (TXREVERTS reverts=expr)? (CREDIT)? (DEBIT)? (POST post=expr)? (RREVERTS rreverts=expr)?
     | (PRE pre=expr)? (TXREVERTS reverts=expr)? (POST post=expr)? (CREDIT)? (DEBIT)? (RREVERTS rreverts=expr)?;
stateMutability : PURE | CONSTANT | VIEW ;
methodDecl : (RECEIVE | FALLBACK | FUNCTION name=iden) LPAREN methodParamList? RPAREN (PUBLIC|PRIVATE)? stateMutability? spec (MODIFIES LBRACK (modifies=rvalueList)? RBRACK)? (MODIFIESA LBRACK (modifies_addrs=rvalueList)? RBRACK)? (RETURNS LPAREN datatype (returnval=iden)? RPAREN)? methodBody # MDecl
        ;
methodParamList : methodParam (COMMA methodParam)* ;
methodParam : datatype name=iden ;
methodBody : LBRACE (varDecl | statement)* returnStatement RBRACE ;
returnStatement : RETURN expr? SEMI ;

varDecl : datatype iden (ASSIGN expr)? SEMI ;

usingForDecl : USING iden FOR (datatype | MUL) SEMI ;

loopVarDecl : datatype iden ASSIGN expr 
            | iden ASSIGN expr ;

statement : //# CompoundStmt
            LBRACE statement* RBRACE
            
            //# PushStmt
	        | arrayName=lvalue DOT PUSH LPAREN value=expr RPAREN SEMI

            //# PopStmt
          | arrayName=lvalue DOT POP LPAREN RPAREN SEMI

            //# DeleteStmt
	        | DELETE LPAREN toDelete=lvalue (COMMA value=expr)? RPAREN SEMI

            //# AssertStmt
          | ASSERT expr (COMMA StringLiteral)? SEMI

            //# CreateStmt
          | assignTo=lvalue ASSIGN NEW iden LPAREN rvalueList? RPAREN SEMI

            // unknown call Statements
          | expr DOT CALL LPAREN rvalueList RPAREN SEMI
          | BOOL iden ASSIGN expr DOT CALL LPAREN rvalueList RPAREN SEMI
          | lvalue ASSIGN expr DOT CALL LPAREN rvalueList RPAREN SEMI

          | expr DOT CALLUINT LPAREN rvalueList RPAREN SEMI
          | UINT iden ASSIGN expr DOT CALLUINT LPAREN rvalueList RPAREN SEMI

          | expr DOT CALLBOOL LPAREN rvalueList RPAREN SEMI
          | BOOL iden ASSIGN expr DOT CALLBOOL LPAREN rvalueList RPAREN SEMI

            //# ExternalContractMethodCallStmt
          | otherContractInstance=lvalue DOT method=iden LPAREN rvalueList? RPAREN SEMI

            //# ExternalContractMethodCallAssignStmt
          | assignTo=lvalue ASSIGN otherContractInstance=lvalue DOT method=iden LPAREN rvalueList? RPAREN SEMI

            //# AssignStmt
          | assignTo=lvalue assignment=ASSIGN rvalue SEMI

            //# IfStmt
          | IF LPAREN expr RPAREN thenBranch=statement elseStatement?

            //# ForStmt
          | FOR LPAREN loopVarDecl (COMMA loopVarDecl)* SEMI expr SEMI expr SEMI RPAREN loopBody=statement

            //# MethodCallStmt
          | method=iden LPAREN rvalueList? RPAREN SEMI

            //# eTransferSendStmt
          | SEND LPAREN contract=expr COMMA ETRANSFER COMMA payload=expr RPAREN SEMI

          // Ether transfer statement
          | to=expr DOT TRANSFER LPAREN amount=expr RPAREN SEMI

            //# SendStmt
          // | SEND LPAREN contract=expr COMMA event=iden COMMA payload=expr (COMMA payload=expr)* RPAREN SEMI
          | EMIT event=iden LPAREN payload=expr (COMMA payload=expr)* RPAREN SEMI

            //# RevertStmt
          | REVERT LPAREN StringLiteral (COMMA rvalueList)? RPAREN SEMI
          ;

elseStatement : ELSE statement ;

lvalue : name=iden                 //# VarLvalue
       | lvalue DOT field=iden     //# NamedTupleLvalue
       | lvalue LBRACK expr RBRACK //# MapOrArrayLvalue
       ;

logcheck : LPAREN event=iden COMMA payload=expr (COMMA payload=expr)* RPAREN
         | LPAREN to=expr COMMA ETRANSFER COMMA payload=expr RPAREN
         ;

expr : primitive                                      //# PrimitiveExpr
     | LPAREN expr RPAREN                             //# ParenExpr
     | iden DOT method=iden LPAREN rvalueList? RPAREN
     | expr DOT field=iden                            //# FieldAccessExpr
     | array=expr LBRACK index=expr RBRACK            //# ArrayMapAccessExpr
     | array=expr DOT LENGTH LPAREN RPAREN            //# ArrayLengthExpr
     | method=iden LPAREN rvalueList? RPAREN          //# MethodCallExpr
     | FORALL LPAREN funParamList RPAREN LPAREN expr RPAREN
     | EXISTS LPAREN funParamList RPAREN LPAREN expr RPAREN
     | op=(SUB | LNOT) expr                           //# UnaryExpr
     | lhs=expr op=(MUL | DIV | MOD) rhs=expr         //# BinExpr
     | SAFEMOD LPAREN lhs=expr COMMA rhs=expr RPAREN  //# SafeMod
     | SAFEDIV LPAREN lhs=expr COMMA rhs=expr RPAREN  //# SafeDiv
     | SAFEMUL LPAREN lhs=expr COMMA rhs=expr RPAREN  //# SafeMul
     | lhs=expr op=(PLUS | SUB) rhs=expr              //# BinExpr
     | SAFEADD LPAREN lhs=expr COMMA rhs=expr RPAREN  //# SafeAdd
     | SAFESUB LPAREN lhs=expr COMMA rhs=expr RPAREN  //# SafeSub
     | lhs=expr op=(LT | GT | GE | LE | IN) rhs=expr  //# BinExpr
     | lhs=expr op=(EQ | NE) rhs=expr                 //# BinExpr
     | lhs=expr op=LAND rhs=expr                      //# BinExpr
     | lhs=expr op=LOR rhs=expr                       //# BinExpr
     | lhs=expr op=(IMPL | BIMPL) rhs=expr            //# ImpliesExpr
     | expr MAPUPD expr (COMMA expr MAPUPD expr)*     //# MapUpdateExpr
     | iden LPAREN expr RPAREN                        //# CastExpr
     | NEW contractName=iden LPAREN rvalueList? RPAREN //# CreateExpr
     | instmap=iden DOT ADD LPAREN NEW contractName=iden LPAREN rvalueList? RPAREN RPAREN //# InstMapAdd
     | ITE LPAREN condition=expr COMMA 
                  thenBranch=expr COMMA 
                  elseBranch=expr RPAREN              //# ite()
     | DEFAULT LPAREN datatype RPAREN
     | logcheck (COLON COLON logcheck)* COLON COLON logName=primitive
     | PAYABLE LPAREN expr RPAREN
//   | contractInstance=lvalue DOT method=iden LPAREN rvalueList? RPAREN // Contract Instance method call
     ;

primitive : iden                                       //# IdenPrimitive
          | VALUE
          | BALANCE
          | SENDER
          | TXGASPRICE | TXORIGIN
          | BCOINBASE | BDIFF | BGASLIMIT | BNUMBER | BTIMESTAMP
          | LOG
          | INT_MIN | INT_MAX | UINT_MAX
          | NEW LPAREN iden RPAREN
          | NEW LPAREN BALANCE RPAREN
          | NEW LPAREN LOG RPAREN
          | BoolLiteral                                //# BoolPrimitive
          | IntLiteral                                 //# IntPrimitive
          | NullLiteral                                //# NullPrimitive
          | StringLiteral
          | THIS                                       //# ThisPrimitive
          | ADDR LPAREN THIS RPAREN
          | ADDR LPAREN iden RPAREN
          ;

unnamedTupleBody : fields+=rvalue COMMA
                 | fields+=rvalue (COMMA fields+=rvalue)+
                 ;

namedTupleBody : names+=iden ASSIGN values+=rvalue COMMA
               | names+=iden ASSIGN values+=rvalue (COMMA names+=iden ASSIGN values+=rvalue)+
               ;

rvalueList : rvalue (COMMA rvalue)* ;
rvalue : expr
       ;
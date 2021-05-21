import argparse
import sys

from antlr4 import *

from CelestialLexer import CelestialLexer
from CelestialParser import CelestialParser
from FStarCodegen import FStarCodegen
from SolidityCodegen import SolidityCodegen
from MyListener import MyListener


def main (argv):
    argParser = argparse.ArgumentParser()
    argParser.add_argument("celestialSource", help="The Celestial source code", type=str)
    argParser.add_argument("--v", "--verificationMode", type=str, choices=["FStar", "VeriSol"], default="FStar", help="Specify whether to use FStar or VeriSol for verification.", required=False)
    argParser.add_argument("--disableFuncBooleanCheck", default=False, action="store_true", help="Setting true allows non-boolean expressions inside functions. Used during compiler development.")
    argParser.add_argument("--printSymbolTable", default=False, action="store_true", help="Displays symbol table. Used during compiler development")
    argParser.add_argument("--outputDir", help="Specify output directory", default=".", type=str)
    argParser.add_argument("--solDir", help="Specify output directory for Solidity file", type=str, required=False)
    argParser.add_argument("--fstDir", help="Specify output directory for FStar file", type=str, required=False)
    arguments = argParser.parse_args()

    sourceCode = FileStream(arguments.celestialSource)
    lexer = CelestialLexer(sourceCode)
    stream = CommonTokenStream(lexer)
    parser = CelestialParser(stream)
    tree = parser.program()

    verificationMode = arguments.v
    print ("Generating input for verification using "+verificationMode)
    outputDirectory = arguments.outputDir
    solidityDirectory = arguments.solDir
    fstarDirectory = arguments.fstDir
    if solidityDirectory is None:
        solidityDirectory = outputDirectory
    if fstarDirectory is None:
        fstarDirectory = outputDirectory

    FSTCodegen = FStarCodegen(fstarDirectory)
    SolCodegen = SolidityCodegen(solidityDirectory, verificationMode)

    listenerOutput = MyListener(FSTCodegen, SolCodegen, arguments.printSymbolTable, arguments.disableFuncBooleanCheck, verificationMode)
    walker = ParseTreeWalker()
    walker.walk(listenerOutput, tree)
    print (arguments.celestialSource + " compiled successfully.")

if __name__ == '__main__':
    print("Celestial v0.0.1")
    main(sys.argv)

﻿

namespace VeriSolRunner
{

    using Microsoft.Extensions.Logging;
    using SolidityAST;
    using BoogieAST;
    using SolToBoogie;
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Runtime.InteropServices;
    using System.Reflection;

    internal class VeriSolExecutor
    {
        private string SolidityFilePath;
        private string SolidityFileDir;
        private string ContractName;
        private string CorralPath;
        private string BoogiePath;
        private string SolcPath;
        private string SolcName;
        private bool TryProof;
        private bool TryRefutation;
        private ILogger Logger;
        private readonly string outFileName = "__SolToBoogieTest_out.bpl";
        private readonly int CorralRecursionLimit;
        private readonly int CorralContextBound = 1; // always 1 for solidity
        private HashSet<Tuple<string, string>> ignoreMethods;

        public VeriSolExecutor(string solidityFilePath, string contractName, string corralPath, string boogiePath, string solcPath, string solcName, int corralRecursionLimit, HashSet<Tuple<string, string>> ignoreMethods, bool tryRefutation, bool tryProofFlag, ILogger logger)
        {
            this.SolidityFilePath = solidityFilePath;
            this.ContractName = contractName;
            this.SolidityFileDir = Path.GetDirectoryName(solidityFilePath);
            Console.WriteLine($"SpecFilesDir = {SolidityFileDir}");
            //if (this.SolidityFileDir.TrimEnd().Equals(string.Empty))
            //{
            //    throw new Exception($"Specify the specFilePath as .\\{solidityFilePath} instead of {solidityFilePath}");
            //}
            this.CorralPath = corralPath;
            this.BoogiePath = boogiePath;
            this.SolcPath = solcPath;
            this.SolcName = solcName;
            this.CorralRecursionLimit = corralRecursionLimit;
            this.ignoreMethods = new HashSet<Tuple<string, string>>(ignoreMethods);
            this.Logger = logger;
            this.TryProof = tryProofFlag;
            this.TryRefutation = tryRefutation;
        }

        public int Execute()
        {
            // call SolToBoogie on specFilePath
            if (!ExecuteSolToBoogie())
            {
                return 1;
            }

            // try to prove first
            if (TryProof && FindProof())
            {
                return 0;
            }

            // run Corral on outFile
            if (TryRefutation && !RunCorralForRefutation())
            {
                return 1;
            }

            return 0;
        }

        private bool FindProof()
        {
            var boogieArgs = new List<string>
            {
                //-doModSetAnalysis -inline:spec (was assert) -noinfer -contractInfer -proc:BoogieEntry_* out.bpl
                //
                $"-doModSetAnalysis",
                $"-inline:assert",
                $"-noinfer",
                $"-contractInfer",
                // main method
                $"-proc:BoogieEntry_*",
                // Boogie file
                outFileName
            };

            var boogieArgString = string.Join(" ", boogieArgs);
            Console.WriteLine($"\n++ Running {BoogiePath} {boogieArgString} ....");
            var boogieOut = RunBinary(BoogiePath, boogieArgString);
            var boogieOutFile = "boogie.txt";
            using (var bFile = new StreamWriter(boogieOutFile))
            {
                bFile.Write(boogieOut);
            }
            Console.WriteLine($"\tFinished Boogie, output in {boogieOutFile}....\n");

            // compare Corral output against expected output
            if (CompareBoogieOutput(boogieOut))
            {
                Console.WriteLine($"\t*** Proof found! Formal Verification successful!");
                return true;
            }
            else
            {
                Console.WriteLine($"\t*** Did not find a proof");
                return false;
            }
        }

        private bool RunCorralForRefutation()
        {
            var corralArgs = new List<string>
            {
                // recursion bound
                $"/recursionBound:{CorralRecursionLimit}",
                // context bound (k)
                $"/k:{CorralContextBound}",
                // main method
                $"/main:CorralEntry_{ContractName}",
                // printing info
                $"/tryCTrace /printDataValues:1",
                // Boogie file
                outFileName
            };

            var corralArgString = string.Join(" ", corralArgs);
            Console.WriteLine($"\n++ Running {CorralPath} {corralArgString} ....");
            var corralOut = RunBinary(CorralPath, corralArgString);
            var corralOutFile = "corral.txt";
            using (var bFile = new StreamWriter(corralOutFile))
            {
                bFile.Write(corralOut);
            }
            Console.WriteLine($"\tFinished Corral, output in {corralOutFile}....\n");

            // compare Corral output against expected output
            if (CompareCorralOutput("Program has no bugs", corralOut))
            {
                Console.WriteLine($"\t*** Formal Verification successful upto {CorralRecursionLimit} transactions");
                return true;
            }
            else
            {
                Console.WriteLine($"\t*** Found a counterexample (see corral_out_trace.txt)");
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    DisplayTraceUsingConcurrencyExplorer();
                }
                return false;
            }
        }

        private static void DisplayTraceUsingConcurrencyExplorer()
        {
            var assemblyLocation = Assembly.GetExecutingAssembly().Location;
            string concExplorerWindowsPath = Path.Combine(
                Path.GetDirectoryName(assemblyLocation),
                "concurrencyExplorer",
                "ConcurrencyExplorer.exe");
            if (!File.Exists(concExplorerWindowsPath))
            {
                throw new Exception($"Cannot find ConcurrencyExplorer.exe at {concExplorerWindowsPath}");
            }
            Console.WriteLine($"\t[To view traces on Windows] {concExplorerWindowsPath} corral_out_trace.txt ");
        }

        private bool ExecuteSolToBoogie()
        {
            // compile the program
            Console.WriteLine($"\n++ Running Solc on {SolidityFilePath}....");

            SolidityCompiler compiler = new SolidityCompiler();
            CompilerOutput compilerOutput = compiler.Compile(SolcPath, SolidityFilePath);

            if (compilerOutput.ContainsError())
            {
                compilerOutput.PrintErrorsToConsole();
                throw new SystemException("Compilation Error");
            }

            // build the Solidity AST from solc output
            AST solidityAST = new AST(compilerOutput, Path.GetDirectoryName(SolidityFilePath));

            // translate Solidity to Boogie
            try
            {
                BoogieTranslator translator = new BoogieTranslator();
                Console.WriteLine($"\n++ Running SolToBoogie to translate Solidity to Boogie....");
                BoogieAST boogieAST = translator.Translate(solidityAST, ignoreMethods, true);

                // dump the Boogie program to a file
                var outFilePath = Path.Combine(SolidityFileDir, outFileName);
                using (var outWriter = new StreamWriter(outFileName))
                {
                    outWriter.WriteLine(boogieAST.GetRoot());
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"VeriSol translation error: {e.Message}");
                return false;
            }
            return true;
        }

        private string RunBinary(string binaryPath, string binaryArguments)
        {
            Process p = new Process();
            p.StartInfo.UseShellExecute = false;
            p.StartInfo.RedirectStandardInput = false;
            p.StartInfo.RedirectStandardOutput = true;
            p.StartInfo.RedirectStandardError = true;
            p.StartInfo.CreateNoWindow = true;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                p.StartInfo.FileName = binaryPath;
                p.StartInfo.Arguments = binaryArguments;
            }
            else
            {
                p.StartInfo.FileName = "mono";
                p.StartInfo.Arguments = $"{binaryPath} {binaryArguments}";
                Console.WriteLine(p.StartInfo.Arguments);
            }
            p.Start();

            string outputBinary = p.StandardOutput.ReadToEnd();
            string errorMsg = p.StandardError.ReadToEnd();
            if (!String.IsNullOrEmpty(errorMsg))
            {
                Console.WriteLine($"Error: {errorMsg}");
            }
            p.StandardOutput.Close();
            p.StandardError.Close();

            // TODO: should set up a timeout here
            // but it seems there is a problem if we execute corral using mono

            return outputBinary;
        }

        private bool CompareCorralOutput(string expected, string actual)
        {
            if (actual == null)
            {
                return false;
            }
            string[] actualList = actual.Split("Boogie verification time");
            if (actualList.Length == 2)
            {
                if (actualList[0].Contains(expected))
                {
                    return true;
                }
            }
            return false;
        }

        private bool CompareBoogieOutput(string actual)
        {
            if (actual == null)
            {
                return false;
            }
            // Boogie program verifier finished with x verified, 0 errors
            if (actual.Contains("Boogie program verifier finished with ") &&
                actual.Contains(" verified, 0 errors"))
            {
                return true;
            }
            return false;
        }

    }
}
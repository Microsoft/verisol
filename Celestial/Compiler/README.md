# Celestial Compiler
### Instructions to compile a Celestial source:
```sh
$ python3 ./main.py <path_to_celestial_source_code> [--fstDir="path_to_output_directory_for_FStar_code"]
[--solDir="path_to_output_directory_for_Solidity_code"] 
```

The F\* code will be generated in the current directory (if `fstDir` is not specified) with the same name as the contract. The Solidity file will be named `contract.sol`.

### Instructions to verify the generated code using F\*
```
fstar --include ../Compiler/lib [--z3rlimit 50] <path_to_generated_FStar_code>
```
One can increase ```z3rlimit``` if required. More details about F* can be found [here](https://github.com/FStarLang/FStar).

### Platform

We have tested Celestial on Ubuntu running on Windows 10, via the Windows Subsystem for Linux. The tool should run as intended on other flavors of Linux, and MacOS.
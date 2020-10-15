# Celestial
***
Celestial is a Microsoft Research project for prototyping a framework for formally verifying smart contracts written in the Solidity language for the Ethereum blockchain. CELESTIAL allows programmers to write expressive functional specifications for their contracts. It translates the contracts and the specifications to F\* to formally verify, against an F\* model of the blockchain semantics, that the contracts meet their specifications. Once the verification succeeds, CELESTIAL performs an erasure of the specifications to generate Solidity code for execution on the Ethereum blockchain. 
Alternatively, one can choose to employ VeriSol as the verification backend. In this case, CELESTIAL generates a VeriSol-compatible Solidity source code to formally verify that the contract meets its specifications. 

We use CELESTIAL to verify several real-world smart contracts from different application domains such as tokens, digital wallets, and governance. 

## Installation
See Install.md

## 
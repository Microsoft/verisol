.DEFAULT_GOAL := help
SHELL := /bin/bash

COMPILER = python3 ../Compiler/main.py 
COMPILER2 = python3 ../Compiler/main.py --v="VeriSol"
FSTAR = fstar.exe --include ../Compiler/lib
TRUFFLE = truffle test
VERISOL = VeriSol 

define TIME
	TIMEFORMAT='Contract(s) verified in %Rs'; time ${FSTAR} $(1) $(2) 2>&1 1>/dev/null
endef

help:
	@echo Usage:
	@echo "    make"
	@echo Targets:
	@echo "    compile  experiment=<experiment_name>     Compiles the Celestial source for the specified experiment and generates the Solidity and FStar versions."
	@echo "    verify   experiment=<experiment_name>     Verifies the generated FStar for the specified experiment."
	@echo "    compileV experiment=<experiment_name>     Compiles the Celestial source for the specified experiment and generates the VeriSol-compatible Solidity version."
	@echo "    verifyV  experiment=<experiment_name>     Verifies the generated Solidity using VeriSol for the specified experiment."
	@echo "    perf     experiment=<experiment_name>     Generates a .csv file with gas comparisions for the specified experiment."
	@echo "    <experiment_name>                         Runs all the above targets for the specified experiment."
	@echo "    all                                       Compiles, verifies and generates gas performance .csv file for all experiments."
	@echo Experiments:
	@echo "    overview"
	@echo "    asset_transfer"
	@echo "    etherdelta"
	@echo "    erc20"
	@echo "    wrapped_ether"
	@echo "    multisig"
	@echo "    simple_auction"
	@echo "    binancecoin"

all: overview asset_transfer erc20 etherdelta wrapped_ether multisig simple_auction binancecoin

compile:
	@if [ '$(experiment)' = 'overview' ]; then \
		${COMPILER} Overview/celestial/Overview.cel --fstDir="../Samples/Overview/fstar" --solDir="../Samples/Overview/contracts/"; \
	elif [ '$(experiment)' = 'asset_transfer' ]; then \
		${COMPILER} AssetTransfer/celestial/AssetTransfer.cel --fstDir="../Samples/AssetTransfer/fstar" --solDir="../Samples/AssetTransfer/contracts/"; \
	elif [ '$(experiment)' = 'etherdelta' ]; then \
		${COMPILER} EtherDelta/celestial/EtherDelta.cel --fstDir="../Samples/EtherDelta/fstar" --solDir="../Samples/EtherDelta/contracts/"; \
	elif [ '$(experiment)' = 'erc20' ]; then \
		${COMPILER} OpenZeppelin/ERC20/celestial/OpenZeppelinERC20.cel --fstDir="../Samples/OpenZeppelin/ERC20/fstar" --solDir="../Samples/OpenZeppelin/ERC20/contracts"; \
	elif [ '$(experiment)' = 'wrapped_ether' ]; then \
		${COMPILER} WrappedEther/celestial/WrappedEther.cel --fstDir="../Samples/WrappedEther/fstar" --solDir="../Samples/WrappedEther/contracts"; \
	elif [ '$(experiment)' = 'multisig' ]; then \
		${COMPILER} ConsenSys/MultiSig/celestial/MultiSig.cel --fstDir="../Samples/ConsenSys/MultiSig/fstar" --solDir="../Samples/ConsenSys/MultiSig/contracts"; \
	elif [ '$(experiment)' = 'simple_auction' ]; then \
		${COMPILER} SimpleAuction/celestial/SimpleAuction.cel --fstDir="../Samples/SimpleAuction/fstar" --solDir="../Samples/SimpleAuction/contracts"; \
	elif [ '$(experiment)' = 'binancecoin' ]; then \
		${COMPILER} BinanceCoin/celestial/BinanceCoin.cel --fstDir="../Samples/BinanceCoin/fstar" --solDir="../Samples/BinanceCoin/contracts"; \
	else \
		echo "Invalid experiment argument to target 'compile'"; \
	fi;

verify:
	@if [ '$(experiment)' = 'overview' ]; then \
		$(call TIME,--z3rlimit 50 --include Overview/fstar,Overview/fstar/SimpleMarket.fst,SimpleMarket); \
	elif [ '$(experiment)' = 'asset_transfer' ]; then \
		$(call TIME,--z3rlimit 50,AssetTransfer/fstar/MarketPlace_Cel.fst,AssetTransfer); \
	elif [ '$(experiment)' = 'etherdelta' ]; then \
		$(call TIME,--z3rlimit 150,EtherDelta/fstar/EtherDelta_Cel.fst,EtherDelta); \
	elif [ '$(experiment)' = 'erc20' ]; then \
		$(call TIME,--z3rlimit 50,OpenZeppelin/ERC20/fstar/ERC20_Cel.fst,ERC20); \
	elif [ '$(experiment)' = 'wrapped_ether' ]; then \
		$(call TIME,--z3rlimit 200,WrappedEther/fstar/WETH9_Cel.fst,Wrapped Ether); \
	elif [ '$(experiment)' = 'multisig' ]; then \
		$(call TIME,--z3rlimit 150,ConsenSys/MultiSig/fstar/MultiSigWalletWithDailyLimit_Cel.fst,ConsenSys MultiSigWallet); \
	elif [ '$(experiment)' = 'simple_auction' ]; then \
		$(call TIME,--z3rlimit 50,SimpleAuction/fstar/SimpleAuction_Cel.fst,Simple Auction); \
	elif [ '$(experiment)' = 'binancecoin' ]; then \
		$(call TIME,--z3rlimit 50,BinanceCoin/fstar/BNB_Cel.fst,BNB); \
	else \
		echo "Invalid experiment argument to target 'verify'"; \
	fi;

compileV:
	@if [ '$(experiment)' = 'overview' ]; then \
		${COMPILER2} Overview/celestial/Overview.cel --fstDir="../Samples/Overview/fstar" --solDir="../Samples/Overview/contracts/"; \
	elif [ '$(experiment)' = 'asset_transfer' ]; then \
		${COMPILER2} AssetTransfer/celestial/AssetTransfer.cel --fstDir="../Samples/AssetTransfer/fstar" --solDir="../Samples/AssetTransfer/contracts/"; \
	elif [ '$(experiment)' = 'etherdelta' ]; then \
		${COMPILER2} EtherDelta/celestial/EtherDelta.cel --fstDir="../Samples/EtherDelta/fstar" --solDir="../Samples/EtherDelta/contracts/"; \
	elif [ '$(experiment)' = 'erc20' ]; then \
		${COMPILER2} OpenZeppelin/ERC20/celestial/OpenZeppelinERC20.cel --fstDir="../Samples/OpenZeppelin/ERC20/fstar" --solDir="../Samples/OpenZeppelin/ERC20/contracts"; \
	elif [ '$(experiment)' = 'wrapped_ether' ]; then \
		${COMPILER2} WrappedEther/celestial/WrappedEther.cel --fstDir="../Samples/WrappedEther/fstar" --solDir="../Samples/WrappedEther/contracts"; \
	elif [ '$(experiment)' = 'multisig' ]; then \
		${COMPILER2} ConsenSys/MultiSig/celestial/MultiSig.cel --fstDir="../Samples/ConsenSys/MultiSig/fstar" --solDir="../Samples/ConsenSys/MultiSig/contracts"; \
	elif [ '$(experiment)' = 'simple_auction' ]; then \
		${COMPILER2} SimpleAuction/celestial/SimpleAuction.cel --fstDir="../Samples/SimpleAuction/fstar" --solDir="../Samples/SimpleAuction/contracts"; \
	elif [ '$(experiment)' = 'binancecoin' ]; then \
		${COMPILER2} BinanceCoin/celestial/BinanceCoin.cel --fstDir="../Samples/BinanceCoin/fstar" --solDir="../Samples/BinanceCoin/contracts"; \
	else \
		echo "Invalid experiment argument to target 'compile'"; \
	fi;

verifyV:
	@if [ '$(experiment)' = 'overview' ]; then \
		${VERISOL} "Overview\contracts\contract.sol" Overview; \
	elif [ '$(experiment)' = 'asset_transfer' ]; then \
		${VERISOL} "AssetTransfer\contracts\contract.sol" MarketPlace_Cel; \
	elif [ '$(experiment)' = 'etherdelta' ]; then \
		${VERISOL} "EtherDelta\contracts\contract.sol" EtherDelta_Cel; \
	elif [ '$(experiment)' = 'erc20' ]; then \
		${VERISOL} "OpenZeppelin\ERC20\contracts\contract.sol" ERC20_Cel; \
	elif [ '$(experiment)' = 'wrapped_ether' ]; then \
		${VERISOL} "WrappedEther\contracts\contract.sol" WETH9_Cel; \
	elif [ '$(experiment)' = 'multisig' ]; then \
		${VERISOL} "ConsenSys\MultiSig\contracts\contract.sol" MultiSigWalletWithDailyLimit_Cel; \
	elif [ '$(experiment)' = 'simple_auction' ]; then \
		${VERISOL} "SimpleAuction\contracts\contract.sol" SimpleAuction_Cel; \
	elif [ '$(experiment)' = 'binancecoin' ]; then \
		${VERISOL} "BinanceCoin\contracts\contract.sol" BNB_Cel; \
	else \
		echo "Invalid experiment argument to target 'verifyWithVeriSol'"; \
	fi;

perf:
	@if [ '$(experiment)' = 'asset_transfer' ]; then \
		cd AssetTransfer && ${TRUFFLE} test/assetTransfer.js; \
	elif [ '$(experiment)' = 'erc20' ]; then \
		cd OpenZeppelin/ERC20 && ${TRUFFLE} test/erc20.js; \
	elif [ '$(experiment)' = 'wrapped_ether' ]; then \
		cd WrappedEther && ${TRUFFLE} test/wrappedEther.js; \
	elif [ '$(experiment)' = 'multisig' ]; then \
		cd ConsenSys/MultiSig && ${TRUFFLE} test/multisig.js; \
	elif [ '$(experiment)' = 'simple_auction' ]; then \
		cd SimpleAuction && ${TRUFFLE} test/simpleAuction.js; \
	else \
		echo "Invalid experiment argument to target 'perf'"; \
	fi;


overview:
	$(MAKE) compile experiment=overview
	$(MAKE) verify experiment=overview

asset_transfer :
	$(MAKE) compile experiment=asset_transfer
	$(MAKE) verify experiment=asset_transfer
	$(MAKE) perf experiment=asset_transfer

etherdelta:
	$(MAKE) compile experiment=etherdelta
	$(MAKE) verify experiment=etherdelta

erc20:
	$(MAKE) compile experiment=erc20
	$(MAKE) verify experiment=erc20
	$(MAKE) perf experiment=erc20

wrapped_ether:
	$(MAKE) compile experiment=wrapped_ether
	$(MAKE) verify experiment=wrapped_ether
	$(MAKE) perf experiment=wrapped_ether

multisig:
	$(MAKE) compile experiment=multisig
	$(MAKE) verify experiment=multisig
	$(MAKE) perf experiment=multisig

simple_auction:
	$(MAKE) compile experiment=simple_auction
	$(MAKE) verify experiment=simple_auction
	$(MAKE) perf experiment=simple_auction

binancecoin:
	$(MAKE) compile experiment=binancecoin
	$(MAKE) verify experiment=binancecoin
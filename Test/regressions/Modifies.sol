pragma solidity ^0.5.0;

// Tests different types of mappings as arguments for VeriSol.Modifies

import "SafeMath.sol";
import "./Libraries/VeriSolContracts.sol";

contract Test {
	using SafeMath for uint256;
	
	mapping (address => uint256) public mapUint256;
	mapping (address => int256) public mapInt256;
	mapping (address => bool) public mapBool;
	
	constructor() public {}
	
	function modUint256(address a, address b) public {
		mapUint256[a] = mapUint256[a] + 3;
		mapUint256[b] = mapUint256[b] + 5;
	}
	
	function modUint256Wrapper(address a, address b) public {
		modUint256(a, b);
		VeriSol.Modifies(mapUint256, [a, b]);
		//VeriSol.Modifies(mapUint256, [a]);     //fails
		//VeriSol.Modifies(mapUint256, [b]);     //fails  
	}
	
	function modInt256(address a, address b, int256 amount) public {	
		mapInt256[a] = mapInt256[a] + amount;
		mapInt256[b] = mapInt256[b] - amount;
	}
	
	function modInt256Wrapper(address a, address b, int256 amount) public {
		modInt256(a, b, amount);
		VeriSol.Modifies(mapInt256, [a, b]);
		//VeriSol.Modifies(mapInt256, [a]);        //fails
		//VeriSol.Modifies(mapInt256, [b]);        //fails
	}
	
	function modBool(address a, address b) public {	
		mapBool[a] = !mapBool[a];
		mapBool[b] = !mapBool[b];
	}
	
	function modBoolWrapper(address a, address b) public {
		modBool(a, b);
		VeriSol.Modifies(mapBool, [a, b]);
		VeriSol.Modifies(mapBool, [a]);     //TODO: passes b/c of the bug
		VeriSol.Modifies(mapBool, [b]);     //TODO: passes b/c of the bug
	}
}
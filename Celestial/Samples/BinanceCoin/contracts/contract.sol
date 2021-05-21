// SPDX-License-Identifier: MIT
pragma solidity^0.6.8;

import { Safe_Arith } from "./Safe_Arith.sol" ; 

contract BNB_Cel {
    event Transfer(address, address, uint);
    event Burn(address, uint);
    event Freeze(address, uint);
    event Unfreeze(address, uint);
    string name;
    string symbol;
    uint8 decimals;
    uint totalSupply;
    address owner;
    mapping (address => uint) balanceOf;
    mapping (address => uint) freezeOf;
    mapping (address => mapping (address => uint)) allowance;

    constructor (uint initialSupply, string tokenName, uint8 decimalUnits, string tokenSymbol) public {
        balanceOf[msg.sender] = initialSupply;
        totalSupply = initialSupply;
        name = tokenName;
        symbol = tokenSymbol;
        decimals = decimalUnits;
        owner = msg.sender;
        return;
    }

    function _transfer (address _to, uint _value) public {
        if (_to == address(0))
        revert ("Preventing transfer to 0x0 address");
        if (_value <= 0)
        revert ("Value is 0");
        if (balanceOf[msg.sender] < _value)
        revert ("Sender doesn't have enough");
        if (balanceOf[_to] > (~uint256(0)) - _value)
        revert ("Overflow!");
        balanceOf[msg.sender] = Safe_Arith.safe_sub(balanceOf[msg.sender], _value);
        balanceOf[_to] = Safe_Arith.safe_add(balanceOf[_to], _value);
        emit Transfer(msg.sender, _to, _value);
        return;
    }

    function approve (address _spender, uint _value) public returns (bool success) {
        if (_value <= 0)
        revert ("value leq 0");
        allowance[msg.sender][_spender] = _value;
        return true;
    }

    function transferFrom (address _from, address _to, uint _value) public returns (bool success) {
        if (_to == address(0))
        revert ("preventing transfer to address 0x00");
        if (_value <= 0)
        revert ("value leq 0");
        if (balanceOf[_from] < _value)
        revert ("sender doesn't have enough!");
        if (balanceOf[_to] > (~uint256(0)) - _value)
        revert ("Overflow!");
        if (_value > allowance[_from][msg.sender])
        revert ("allowance check failed");
        balanceOf[_from] = Safe_Arith.safe_sub(balanceOf[_from], _value);
        balanceOf[_to] = Safe_Arith.safe_add(balanceOf[_to], _value);
        allowance[_from][msg.sender] = Safe_Arith.safe_sub(allowance[_from][msg.sender], _value);
        emit Transfer(_from, _to, _value);
        return true;
    }

    function burn (uint _value) public returns (bool success) {
        if (balanceOf[msg.sender] < _value)
        revert ("sender doesn't have enough!");
        if (_value <= 0)
        revert ("value leq 0");
        balanceOf[msg.sender] = Safe_Arith.safe_sub(balanceOf[msg.sender], _value);
        totalSupply = Safe_Arith.safe_sub(totalSupply, _value);
        emit Burn(msg.sender, _value);
        return true;
    }

    function freeze (uint _value) public returns (bool success) {
        if (balanceOf[msg.sender] < _value)
        revert ("sender doesn't have enough!");
        if (_value <= 0)
        revert ("value leq 0");
        balanceOf[msg.sender] = Safe_Arith.safe_sub(balanceOf[msg.sender], _value);
        freezeOf[msg.sender] = Safe_Arith.safe_add(freezeOf[msg.sender], _value);
        emit Freeze(msg.sender, _value);
        return true;
    }

    function unfreeze (uint _value) public returns (bool success) {
        if (freezeOf[msg.sender] < _value)
        revert ("sender doesn't have enough!");
        if (_value <= 0)
        revert ("value leq 0");
        freezeOf[msg.sender] = Safe_Arith.safe_sub(freezeOf[msg.sender], _value);
        balanceOf[msg.sender] = Safe_Arith.safe_add(balanceOf[msg.sender], _value);
        emit Unfreeze(msg.sender, _value);
        return true;
    }

    function withdrawEther (uint amount) public {
        if (msg.sender != owner)
        revert ("sender is not owner");
        if (amount > address(this).balance)
        revert ("Insufficient balance");
        payable(owner).transfer(amount);
        return;
    }

    receive () external payable {
        return;
    }
}
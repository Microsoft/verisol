// SPDX-License-Identifier: MIT
pragma solidity^0.6.8;

import { Safe_Arith } from "./Safe_Arith.sol" ; 

contract WETH9_Cel {
    event Approval(address, address, uint);
    event Transfer(address, address, uint);
    event Deposit(address, uint);
    event Withdrawal(address, uint);
    string name = "Wrapped Ether";
    string symbol = "WETH";
    uint decimals = 18;
    mapping (address => uint) balanceOf;
    mapping (address => mapping (address => uint)) allowance;
    uint totalBalance;

    function deposit () public payable {
        totalBalance = Safe_Arith.safe_add(totalBalance, msg.value);
        balanceOf[msg.sender] = balanceOf[msg.sender] + msg.value;
        emit Deposit(msg.sender, msg.value);
        return;
    }

    function withdraw (uint _wad) public {
        if (balanceOf[msg.sender] < _wad)
        {
            revert ("Insufficient balance");
        }
        msg.sender.transfer(_wad);
        emit Withdrawal(msg.sender, _wad);
        if (address(this).balance < totalBalance)
        {
            balanceOf[msg.sender] = balanceOf[msg.sender] - _wad;
            totalBalance = totalBalance - _wad;
        }
        return;
    }

    function totalSupply () public returns (uint a) {
        a = address(this).balance;
        return a;
    }

    function approve (address _guy, uint _wad) public returns (bool) {
        allowance[msg.sender][_guy] = _wad;
        emit Approval(msg.sender, _guy, _wad);
        return true;
    }

    function transferFrom (address _src, address _dst, uint _wad) public returns (bool) {
        if (_src == _dst)
        {
            revert ("Redundant transfer");
        }
        if (balanceOf[_src] < _wad)
        {
            revert ("Insufficient balance");
        }
        if (_src != msg.sender && allowance[_src][msg.sender] != (~uint256(0)))
        {
            if (allowance[_src][msg.sender] < _wad)
            {
                revert ("Insufficient allowance");
            }
            allowance[_src][msg.sender] = allowance[_src][msg.sender] - _wad;
        }
        balanceOf[_src] = balanceOf[_src] - _wad;
        balanceOf[_dst] = balanceOf[_dst] + _wad;
        emit Transfer(_src, _dst, _wad);
        return true;
    }

    function _transfer (address _dst, uint _wad) public returns (bool) {
        return transferFrom(msg.sender, _dst, _wad);
    }

    receive () external payable {
        totalBalance = Safe_Arith.safe_add(totalBalance, msg.value);
        balanceOf[msg.sender] = balanceOf[msg.sender] + msg.value;
        emit Deposit(msg.sender, msg.value);
        return;
    }
}
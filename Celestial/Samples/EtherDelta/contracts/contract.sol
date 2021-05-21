// SPDX-License-Identifier: MIT
pragma solidity^0.6.8;

import { Safe_Arith } from "./Safe_Arith.sol" ; 
import { Call } from "./Call.sol" ; 

contract EtherDelta_Cel {
    event Deposit(address, address, uint, uint);
    event Withdraw(address, address, uint, uint);
    event Order(address, uint, address, uint, uint, uint, address);
    event Cancel(address, uint, address, uint, uint, uint, address, uint8, bytes32, bytes32);
    event Trade(address, uint, address, uint, address, address);
    address admin;
    address feeAccount;
    address accountLevelsAddr;
    uint feeMake;
    uint feeTake;
    uint feeRebate;
    mapping (address => mapping (address => uint)) tokens;
    mapping (address => mapping (bytes32 => bool)) orders;
    mapping (address => mapping (bytes32 => uint)) orderFills;
    uint totalBalance;
    bool _lock_;
    using Call for address ; 

    constructor (address admin_, address feeAccount_, uint feeMake_, uint feeTake_, uint feeRebate_) public {
        admin = admin_;
        feeAccount = feeAccount_;
        feeMake = feeMake_;
        feeTake = feeTake_;
        feeRebate = feeRebate_;
        return;
    }

    fallback () external {
        revert ("");
        return;
    }

    function changeAdmin (address admin_) public {
        if (_lock_)
        {
            revert ("Reentrancy detected");
        }
        if (msg.sender != admin)
        {
            revert ("");
        }
        admin = admin_;
        return;
    }

    function changeFeeAccount (address feeAccount_) public {
        if (_lock_)
        {
            revert ("Reentrancy detected");
        }
        if (msg.sender != admin)
        {
            revert ("");
        }
        feeAccount = feeAccount_;
        return;
    }

    function changeFeeMake (uint feeMake_) public {
        if (_lock_)
        {
            revert ("Reentrancy detected");
        }
        if (msg.sender != admin || feeMake_ > feeMake)
        {
            revert ("");
        }
        feeMake = feeMake_;
        return;
    }

    function changeFeeTake (uint feeTake_) public {
        if (_lock_)
        {
            revert ("Reentrancy detected");
        }
        if (msg.sender != admin || feeTake_ > feeTake || feeTake_ < feeRebate)
        {
            revert ("");
        }
        feeTake = feeTake_;
        return;
    }

    function changeFeeRebate (uint feeRebate_) public {
        if (_lock_)
        {
            revert ("Reentrancy detected");
        }
        if (msg.sender != admin || feeRebate_ < feeRebate || feeRebate_ > feeTake)
        {
            revert ("invalid");
        }
        feeRebate = feeRebate_;
        return;
    }

    function deposit () public payable {
        if (_lock_)
        {
            revert ("Reentrancy detected");
        }
        totalBalance = Safe_Arith.safe_add(totalBalance, msg.value);
        tokens[address(0)][msg.sender] = tokens[address(0)][msg.sender] + msg.value;
        emit Deposit(address(0), msg.sender, msg.value, tokens[address(0)][msg.sender]);
        return;
    }

    function withdraw (uint amount) public {
        if (_lock_)
        {
            revert ("Reentrancy detected");
        }
        if (tokens[address(0)][msg.sender] < amount)
        {
            revert ("Insufficient balance");
        }
        msg.sender.transfer(amount);
        if (address(this).balance == totalBalance)
        {
            revert ("Ether sent to self");
        }
        tokens[address(0)][msg.sender] = tokens[address(0)][msg.sender] - amount;
        totalBalance = totalBalance - amount;
        emit Withdraw(address(0), msg.sender, amount, tokens[address(0)][msg.sender]);
        return;
    }

    function depositToken (address token, uint amount) public {
        if (_lock_)
        {
            revert ("Reentrancy detected");
        }
        if (token == address(0) || amount > (~uint256(0)) - tokens[token][msg.sender])
        {
            revert ("Invalid token type or overflow");
        }
        _lock_ = true;
        bool tokenTxStatus = token.call_bool(abi.encodeWithSignature("transferFrom(address,address,uint)", msg.sender, address(this), amount));
        _lock_ = false;
        if (tokenTxStatus == false)
        {
            revert ("");
        }
        if (totalBalance != address(this).balance)
        {
            revert ("Unexpected Ether transferred to self");
        }
        totalBalance = address(this).balance;
        tokens[token][msg.sender] = tokens[token][msg.sender] + amount;
        emit Deposit(token, msg.sender, amount, tokens[token][msg.sender]);
        return;
    }

    function withdrawToken (address token, uint amount) public {
        if (_lock_)
        {
            revert ("Reentrancy detected");
        }
        if (token == address(0) || tokens[token][msg.sender] < amount)
        {
            revert ("Invalid token type or overflow");
        }
        _lock_ = true;
        bool tokenTxStatus = token.call_bool(abi.encodeWithSignature("transfer(address,uint)", msg.sender, amount));
        _lock_ = false;
        if (tokenTxStatus == false)
        {
            revert ("");
        }
        if (totalBalance != address(this).balance)
        {
            revert ("Unexpected Ether transferred to self");
        }
        totalBalance = address(this).balance;
        tokens[token][msg.sender] = tokens[token][msg.sender] - amount;
        emit Withdraw(msg.sender, token, amount, tokens[token][msg.sender]);
        return;
    }

    function balanceOf (address token, address user) public returns (uint) {
        if (_lock_)
        {
            revert ("Reentrancy detected");
        }
        return tokens[token][user];
    }

    function order (address tokenGet, uint amountGet, address tokenGive, uint amountGive, uint expires, uint nonce) public {
        if (_lock_)
        {
            revert ("Reentrancy detected");
        }
        bytes32 hash = sha256(abi.encodePacked(this, tokenGet, amountGet, tokenGive, amountGive, expires, nonce));
        orders[msg.sender][hash] = true;
        emit Order(tokenGet, amountGet, tokenGive, amountGive, expires, nonce, msg.sender);
        return;
    }

    function availableVolume (address tokenGet, uint amountGet, address tokenGive, uint amountGive, uint expires, uint nonce, address user, uint8 v, bytes32 r, bytes32 s) public returns (uint ret) {
        if (_lock_)
        {
            revert ("Reentrancy detected");
        }
        uint available1;
        uint available2;
        bytes32 hash = sha256(abi.encodePacked(this, tokenGet, amountGet, tokenGive, amountGive, expires, nonce));
        if (! ((orders[user][hash] || ecrecover(keccak256(abi.encodePacked("\x19Ethereum Signed Message:\n32", hash)), v, r, s) == user) && block.number <= expires))
        {
            ret = 0;
        }
        else
        {
            if (amountGive == 0)
            revert ("Divide by 0");
            available1 = Safe_Arith.safe_sub(amountGet, orderFills[user][hash]);
            available2 = Safe_Arith.safe_mul(tokens[tokenGive][user], amountGet) / amountGive;
            if (available1 < available2)
            ret = available1;
            else
            ret = available2;
        }
        return ret;
    }

    function testTrade (address tokenGet, uint amountGet, address tokenGive, uint amountGive, uint expires, uint nonce, address user, uint8 v, bytes32 r, bytes32 s, uint amount, address _sender) public returns (bool ret) {
        if (_lock_)
        {
            revert ("Reentrancy detected");
        }
        uint availableVol = availableVolume(tokenGet, amountGet, tokenGive, amountGive, expires, nonce, user, v, r, s);
        if (! (tokens[tokenGet][_sender] >= amount && availableVol >= amount))
        ret = false;
        else
        ret = true;
        return ret;
    }

    function amountFilled (address tokenGet, uint amountGet, address tokenGive, uint amountGive, uint expires, uint nonce, address user, uint8 v, bytes32 r, bytes32 s) public returns (uint) {
        if (_lock_)
        {
            revert ("Reentrancy detected");
        }
        bytes32 hash = sha256(abi.encodePacked(this, tokenGet, amountGet, tokenGive, amountGive, expires, nonce));
        return orderFills[user][hash];
    }

    function cancelOrder (address tokenGet, uint amountGet, address tokenGive, uint amountGive, uint expires, uint nonce, uint8 v, bytes32 r, bytes32 s) public {
        if (_lock_)
        {
            revert ("Reentrancy detected");
        }
        bytes32 hash = sha256(abi.encodePacked(this, tokenGet, amountGet, tokenGive, amountGive, expires, nonce));
        if (! (orders[msg.sender][hash] || ecrecover(keccak256(abi.encodePacked("\x19Ethereum Signed Message:\n32", hash)), v, r, s) == msg.sender))
        revert ("");
        orderFills[msg.sender][hash] = amountGet;
        emit Cancel(tokenGet, amountGet, tokenGive, amountGive, expires, nonce, msg.sender, v, r, s);
        return;
    }
}
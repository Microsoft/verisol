// SPDX-License-Identifier: MIT
pragma solidity^0.6.8;

import { Safe_Arith } from "./Safe_Arith.sol" ; 

contract MultiSigWalletWithDailyLimit_Cel {
    struct Transaction
    {
        address destination;
        uint tval;
        bool executed;
    }
    event Confirmation(address, uint);
    event Revocation(address, uint);
    event Submission(uint);
    event Execution(uint);
    event OwnerAddition(address);
    event OwnerRemoval(address);
    event RequirementChange(uint);
    event DailyLimitChange(uint);
    uint MAX_OWNER_COUNT = 50;
    mapping (uint => Transaction) transactions;
    mapping (uint => uint) confirmationCounts;
    mapping (uint => mapping (address => bool)) confirmations;
    mapping (address => bool) isOwner;
    uint ownerCount;
    uint required;
    uint transactionCount;
    bool walletActive;
    bool tx_isConfirmed;
    bool tx_isUnderLimit;
    uint dailyLimit;
    uint lastDay;
    uint spentToday;

    receive () external payable {
        return;
    }

    constructor (address initial_owner, uint _required, uint _dailyLimit) public {
        isOwner[initial_owner] = true;
        ownerCount = 1;
        required = _required;
        MAX_OWNER_COUNT = 50;
        dailyLimit = _dailyLimit;
        return;
    }

    function addOwner (address owner) public {
        uint target_ownerCount = Safe_Arith.safe_add(ownerCount, 1);
        if (isOwner[owner] || owner == address(0) || target_ownerCount > MAX_OWNER_COUNT)
        {
            revert ("Invalid owner addition");
        }
        isOwner[owner] = true;
        ownerCount = ownerCount + 1;
        if (ownerCount >= required)
        {
            walletActive = true;
        }
        emit OwnerAddition(owner);
        return;
    }

    function removeOwner (address owner) public {
        if (! isOwner[owner])
        {
            revert ("Invalid owner removal");
        }
        ownerCount = Safe_Arith.safe_sub(ownerCount, 1);
        isOwner[owner] = false;
        if (ownerCount < required)
        {
            walletActive = false;
        }
        emit OwnerRemoval(owner);
        return;
    }

    function replaceOwner (address owner, address newOwner) public {
        if (! isOwner[owner] || isOwner[newOwner])
        {
            revert ("Invalid owner replacement");
        }
        isOwner[owner] = false;
        isOwner[newOwner] = true;
        emit OwnerRemoval(owner);
        emit OwnerAddition(newOwner);
        return;
    }

    function changeRequirement (uint _required) public {
        required = _required;
        if (required > ownerCount)
        {
            walletActive = false;
        }
        emit RequirementChange(_required);
        return;
    }

    function addTransaction (address _destination, uint _val) private returns (uint transactionId) {
        transactionId = transactionCount;
        transactions[transactionId] = Transaction(_destination, _val, false);
        confirmationCounts[transactionId] = 0;
        emit Submission(transactionId);
        return transactionId;
    }

    function isConfirmed (uint transactionId) private returns (bool ret) {
        if (confirmationCounts[transactionId] == required)
        {
            ret = true;
        }
        else
        {
            ret = false;
        }
        return ret;
    }

    function isUnderLimit (uint _amount) private returns (bool ret) {
        uint t = Safe_Arith.safe_add(lastDay, 86400);
        if (block.timestamp > t)
        {
            lastDay = block.timestamp;
            spentToday = 0;
        }
        if (_amount > (~uint256(0)) - spentToday)
        {
            ret = false;
        }
        else
        if (! ret)
        {
            if (spentToday + _amount > dailyLimit)
            {
                ret = false;
            }
            else
            {
                ret = true;
            }
        }
        return ret;
    }

    function executeTransaction (uint transactionId) public {
        if (transactionId >= transactionCount || transactions[transactionId].executed || address(this).balance < transactions[transactionId].tval)
        {
            revert ("invalid");
        }
        Transaction storage trx = transactions[transactionId];
        tx_isConfirmed = isConfirmed(transactionId);
        tx_isUnderLimit = isUnderLimit(trx.tval);
        if (tx_isConfirmed || tx_isUnderLimit)
        {
            if (! tx_isConfirmed)
            {
                spentToday = spentToday + trx.tval;
            }
            transactions[transactionId].executed = true;
            payable(transactions[transactionId].destination).transfer(trx.tval);
            emit Execution(transactionId);
        }
        return;
    }

    function confirmTransaction (uint transactionId) public {
        if (! walletActive || ! isOwner[msg.sender] || transactionId >= transactionCount || confirmations[transactionId][msg.sender])
        {
            revert ("invalid");
        }
        confirmationCounts[transactionId] = Safe_Arith.safe_add(confirmationCounts[transactionId], 1);
        confirmations[transactionId][msg.sender] = true;
        emit Confirmation(msg.sender, transactionId);
        executeTransaction(transactionId);
        return;
    }

    function submitTransaction (address _dest, uint _amount) public returns (uint transactionId) {
        if (! walletActive || ! isOwner[msg.sender] || _dest == address(0))
        {
            revert ("invalid");
        }
        transactionId = addTransaction(_dest, _amount);
        transactionCount = Safe_Arith.safe_add(transactionCount, 1);
        return transactionId;
    }

    function revokeConfirmation (uint transactionId) public {
        if (! isOwner[msg.sender] || ! confirmations[transactionId][msg.sender] || transactionId > transactionCount || transactions[transactionId].executed)
        {
            revert ("Invalid");
        }
        confirmations[transactionId][msg.sender] = false;
        confirmationCounts[transactionId] = Safe_Arith.safe_sub(confirmationCounts[transactionId], 1);
        emit Revocation(msg.sender, transactionId);
        return;
    }

    function getConfirmationCount (uint transactionId) public returns (uint) {
        return confirmationCounts[transactionId];
    }

    function changeDailyLimit (uint _dailyLimit) public {
        dailyLimit = _dailyLimit;
        emit DailyLimitChange(_dailyLimit);
        return;
    }

    function calcMaxWithdraw () public returns (uint) {
        uint ret;
        uint endTime = Safe_Arith.safe_add(lastDay, 86400);
        if (block.timestamp > endTime)
        {
            ret = dailyLimit;
        }
        else
        if (dailyLimit < spentToday)
        {
            ret = 0;
        }
        else
        {
            ret = dailyLimit - spentToday;
        }
        return ret;
    }
}
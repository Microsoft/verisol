// SPDX-License-Identifier: MIT
pragma solidity^0.6.8;

import { Safe_Arith } from "./Safe_Arith.sol" ; 

contract SimpleAuction_Cel {
    event HighestBidIncreased(address, uint);
    event AuctionEnded(address, uint);
    address beneficiary;
    uint auctionEndTime;
    address highestBidder;
    uint highestBid;
    mapping (address => uint) pendingReturns;
    bool ended;
    uint totalReturns;

    constructor (uint _biddingTime, address _beneficiary) public {
        beneficiary = _beneficiary;
        auctionEndTime = Safe_Arith.safe_add(block.timestamp, _biddingTime);
        return;
    }

    function bid () public payable {
        if (block.timestamp > auctionEndTime)
        revert ("Auction already ended.");
        if (msg.value <= highestBid)
        revert ("There already is a higher bid.");
        totalReturns = Safe_Arith.safe_add(totalReturns, msg.value);
        if (highestBid != 0)
        pendingReturns[highestBidder] = Safe_Arith.safe_add(pendingReturns[highestBidder], highestBid);
        highestBidder = msg.sender;
        highestBid = msg.value;
        emit HighestBidIncreased(msg.sender, msg.value);
        return;
    }

    function withdraw () public returns (bool) {
        uint amount = pendingReturns[msg.sender];
        if (amount > 0 && address(this).balance >= pendingReturns[msg.sender])
        {
            msg.sender.transfer(amount);
            if (address(this).balance < totalReturns)
            {
                pendingReturns[msg.sender] = 0;
                totalReturns = totalReturns - amount;
            }
        }
        return true;
    }

    function auctionEnd () public {
        if (block.timestamp < auctionEndTime)
        revert ("Auction not yet ended.");
        if (ended)
        revert ("auctionEnd has already been called.");
        ended = true;
        emit AuctionEnded(highestBidder, highestBid);
        payable(beneficiary).transfer(highestBid);
        if (address(this).balance < totalReturns)
        totalReturns = totalReturns - highestBid;
        return;
    }
}
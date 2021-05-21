// SPDX-License-Identifier: MIT
pragma solidity^0.6.8;

import { Safe_Arith } from "./Safe_Arith.sol" ; 

contract Item {
    address seller;
    uint price;
    address market;

    constructor (address _s, address _m, uint _p) public {
        seller = _s;
        price = _p;
        market = _m;
        return;
    }

    function getSeller () public returns (address s) {
        return seller;
    }

    function getPrice () public returns (uint ret) {
        return price;
    }
}

contract SimpleMarket {
    event eNewItem(address, address);
    event eItemSold(address, address);
    mapping (address => uint) sellerCredits;
    mapping (Item => bool) itemsToSell;

    function get_from_itemsToSell (Item i) private returns (Item) {
        if (itemsToSell[i]) return i;
    }

    function add_to_itemsToSell (Item i) private returns (Item) {
        itemsToSell[i] = true;
        return i;
    }
    uint totalCredits;

    function sell (uint price) public returns (address itemId) {
        itemId = address(add_to_itemsToSell(new Item(address(this), msg.sender, price)));
        return itemId;
    }

    function buy (address itemId) public payable returns (address seller) {
        Item item = get_from_itemsToSell(Item(payable(itemId)));
        if (address(item) == address(0))
        {
            revert ("No such item");
        }
        uint iPrice;
        item.getPrice();
        if (msg.value != iPrice)
        {
            revert ("Incorrect price");
        }
        item.getSeller();
        totalCredits = Safe_Arith.safe_add(totalCredits, msg.value);
        sellerCredits[seller] = sellerCredits[seller] + msg.value;
        emit eItemSold(msg.sender, itemId);
        return seller;
    }

    function withdraw (uint amount) public {
        if (sellerCredits[msg.sender] >= amount)
        {
            msg.sender.transfer(amount);
            sellerCredits[msg.sender] = sellerCredits[msg.sender] - amount;
            totalCredits = totalCredits - amount;
        }
        else
        {
            revert ("Insufficient balance");
        }
        return;
    }
}
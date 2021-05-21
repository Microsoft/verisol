// SPDX-License-Identifier: MIT
pragma solidity^0.6.8;

import { Safe_Arith } from "./Safe_Arith.sol" ; 

contract MarketPlace_Cel {
    enum state
    {
        marketPlace_Active, marketPlace_OfferPlaced, marketPlace_NotionalAccept, marketPlace_BuyerAccept, marketPlace_SellerAccept, marketPlace_Accept
    }
    event eMakeOffer(address, uint);
    event eAcceptOffer(address, uint);
    address seller;
    address buyer;
    uint sellingPrice;
    uint buyingPrice;
    state contractCurrentState;

    constructor (address _seller, address _buyer) public {
        seller = _seller;
        buyer = _buyer;
        contractCurrentState = state.marketPlace_Active;
        return;
    }

    function makeOffer (uint _sellingPrice) public {
        if (msg.sender != seller)
        {
            revert ("");
        }
        if (contractCurrentState != state.marketPlace_Active)
        {
            revert ("<makeOffer> function invoked in invalid state");
        }
        sellingPrice = _sellingPrice;
        contractCurrentState = state.marketPlace_OfferPlaced;
        emit eMakeOffer(buyer, sellingPrice);
        return;
    }

    function modifyOffer (bool _increase, uint _change) public {
        if (msg.sender != seller)
        {
            revert ("");
        }
        if (! (contractCurrentState == state.marketPlace_OfferPlaced && msg.sender == seller))
        {
            revert ("<modifyOffer> function invoked in invalid state");
        }
        if (_increase)
        {
            sellingPrice = Safe_Arith.safe_add(sellingPrice, _change);
        }
        else
        {
            sellingPrice = Safe_Arith.safe_sub(sellingPrice, _change);
        }
        return;
    }

    function rejectOffer () public {
        if (msg.sender != buyer)
        {
            revert ("");
        }
        if (! (contractCurrentState == state.marketPlace_OfferPlaced && msg.sender == buyer))
        {
            revert ("<rejectOffer> function invoked in invalid state");
        }
        contractCurrentState = state.marketPlace_Active;
        return;
    }

    function acceptOffer () public payable {
        if (contractCurrentState != state.marketPlace_OfferPlaced || msg.sender != buyer)
        {
            revert ("<acceptOffer> function invoked in invalid state");
        }
        if (msg.value >= sellingPrice)
        {
            buyingPrice = msg.value;
            emit eAcceptOffer(seller, buyingPrice);
            contractCurrentState = state.marketPlace_NotionalAccept;
        }
        return;
    }

    function accept () public {
        if (! (contractCurrentState == state.marketPlace_NotionalAccept || contractCurrentState == state.marketPlace_BuyerAccept || contractCurrentState == state.marketPlace_SellerAccept))
        {
            revert ("<accept> function invoked in invalid state");
        }
        if (contractCurrentState == state.marketPlace_NotionalAccept && msg.sender == buyer)
        {
            contractCurrentState = state.marketPlace_BuyerAccept;
        }
        else
        if (contractCurrentState == state.marketPlace_NotionalAccept && msg.sender == seller)
        {
            contractCurrentState = state.marketPlace_SellerAccept;
        }
        else
        if (contractCurrentState == state.marketPlace_BuyerAccept && msg.sender == seller)
        {
            contractCurrentState = state.marketPlace_Accept;
        }
        else
        if (contractCurrentState == state.marketPlace_SellerAccept && msg.sender == buyer)
        {
            contractCurrentState = state.marketPlace_Accept;
        }
        return;
    }

    function withdraw () public {
        if (msg.sender != seller)
        {
            revert ("");
        }
        if (! (contractCurrentState == state.marketPlace_Accept && msg.sender == seller))
        {
            revert ("<withdraw> function invoked in invalid state");
        }
        if (address(this).balance < buyingPrice)
        {
            revert ("Insufficient balance");
        }
        payable(seller).transfer(buyingPrice);
        return;
    }
}
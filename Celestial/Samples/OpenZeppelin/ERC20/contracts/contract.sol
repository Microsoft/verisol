// SPDX-License-Identifier: MIT
pragma solidity^0.6.8;


contract ERC20_Cel {
    mapping (address => uint) balances;
    mapping (address => mapping (address => uint)) allowances;
    uint totalSupply;

    function _msgSender () private view returns (address ret) {
        return msg.sender;
    }

    function getTotalSupply () public returns (uint) {
        return totalSupply;
    }

    function balanceOf (address account) public returns (uint) {
        return balances[account];
    }

    function _transfer (address _from, address _to, uint _amount) private {
        if (_from != _to)
        {
            balances[_from] = balances[_from] - (_amount);
            balances[_to] = balances[_to] + (_amount);
        }
        return;
    }

    function mint (address _account, uint _amount) public {
        if (_account == address(0))
        {
            revert ("ERC20: mint to the zero address");
        }
        if (totalSupply <= (~uint256(0)) - _amount && balances[_account] <= (~uint256(0)) - _amount)
        {
            totalSupply = totalSupply + _amount;
            balances[_account] = balances[_account] + _amount;
        }
        else
        {
            revert ("Overflow");
        }
        return;
    }

    function burn (address _account, uint _amount) public {
        if (_account == address(0))
        {
            revert ("ERC20: burn from the zero address");
        }
        if (balances[_account] >= _amount && totalSupply >= _amount)
        {
            totalSupply = totalSupply - _amount;
            balances[_account] = balances[_account] - _amount;
        }
        else
        {
            revert ("Underflow");
        }
        return;
    }

    function _approve (address _owner, address _spender, uint _amount) private {
        allowances[_owner][_spender] = _amount;
        return;
    }

    function burnFrom (address _account, uint _amount) public {
        burn(_account, _amount);
        if (_account != address(0) && msg.sender != address(0) && allowances[_account][msg.sender] >= _amount)
        {
            _approve(_account, msg.sender, allowances[_account][msg.sender] - _amount);
        }
        return;
    }

    function transfer_ (address _to, uint _amount) public returns (bool) {
        if (_to == address(0))
        {
            revert ("Sender/Recipient must be non-null");
        }
        if (balances[msg.sender] >= _amount && balances[_to] <= (~uint256(0)) - _amount)
        {
            _transfer(msg.sender, _to, _amount);
        }
        return true;
    }

    function allowance (address _owner, address _spender) public returns (uint) {
        return allowances[_owner][_spender];
    }

    function approve (address _spender, uint _amount) public returns (bool) {
        if (_spender != address(0))
        {
            _approve(msg.sender, _spender, _amount);
        }
        return true;
    }

    function transferFrom (address _from, address _to, uint _amount) public returns (bool) {
        if (_from == address(0) || _to == address(0))
        {
            revert ("<ErrorLog> from/to addresses are null");
        }
        if (balances[_from] >= _amount && balances[_to] <= (~uint256(0)) - _amount)
        {
            _transfer(_from, _to, _amount);
        }
        else
        {
            revert ("<ErrorLog> Underflow/Overflow");
        }
        if (allowances[_from][msg.sender] >= _amount)
        {
            _approve(_from, msg.sender, allowances[_from][msg.sender] - _amount);
        }
        else
        {
            revert ("<ErrorLog> Allowance insufficient");
        }
        return true;
    }

    function increaseAllowance (address _spender, uint _addedValue) public returns (bool) {
        if (_spender != address(0) && allowances[msg.sender][_spender] <= (~uint256(0)) - _addedValue)
        {
            _approve(msg.sender, _spender, allowances[msg.sender][_spender] + _addedValue);
        }
        return true;
    }

    function decreaseAllowance (address _spender, uint _subtractedValue) public returns (bool) {
        if (_spender != address(0) && allowances[msg.sender][_spender] >= _subtractedValue)
        {
            _approve(msg.sender, _spender, allowances[msg.sender][_spender] - _subtractedValue);
        }
        return true;
    }
}
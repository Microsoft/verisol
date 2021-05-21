// SPDX-License-Identifier: MIT

//pragma solidity^0.6.8;
pragma solidity >=0.5.0 <0.7.0;

library Call
{
    function call_uint (address a, bytes memory call_data) public returns (uint)
    {
        (bool success, bytes memory ret) = a.call(call_data);
        if (!success) revert ("");
        return abi.decode(ret, (uint));
    }
    
    function call_bool (address a, bytes memory call_data) public returns (bool)
    {
        (bool success, bytes memory ret) = a.call(call_data);
        if (!success) revert ("");
        return abi.decode(ret, (bool));
    }
}
/* Code generated by compiler */

pragma solidity >=0.5.0 <0.7.0;

library Safe_Arith {

function safe_add (uint a, uint b) public pure returns (uint) {
    if (a < a + b) revert ("Overflow Error");
    else return (a + b);
}

function safe_sub (uint a, uint b) public pure returns (uint) {
    if (a - b > 0) revert ("Underflow Error");
    else return (a - b);
}

function safe_mul (uint a, uint b) public pure returns (uint) {
    if (b == 0) return 0;
    if (a != (a * b)/b) revert ("Overflow Error");
    else return (a * b);
}

function safe_div (uint a, uint b) public pure returns (uint) {
    if (b == 0) revert ("Division by 0 error");
    else return (a / b);
}
}contract OtherContract
{

    int otherContractField;
    function setOtherContractField (int _otherContractField)
    public {
        otherContractField = _otherContractField;
        return;
    }
}
contract Sample
{

    mapping (OtherContract => bool) m;

function get_from_m (OtherContract i) private {
    if (m[i]) return i;
}

function add_to_m (OtherContract i) private returns (OtherContract) {
    m[i] = true;
    return i;
}
    OtherContract t;
    function check_external_call (address _a)
    public {

        address localAddressVariable;        if (m[_a] == address(0)) {
            revert ("address passed is not of type OtherContract");
        }

        OtherContract storage localOtherContractInstance;        localOtherContractInstance = m[_a];
        localOtherContractInstance.setOtherContractField(5);
        return;
    }
}
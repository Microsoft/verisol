pragma solidity >=0.4.24<0.6.0;

/**
 * Library for VeriSol Code Contracts
 * 
 * Mechanism to add various specification constructs
 *   loop invariants
 *   contract invariants
 *   precondition
 *   postcondition
 *   
 * Also support enriching the assertion language 
 * 
 */
library VeriSol {

   /*
    ********************************************************************
    *  Specification mechanisms
    ********************************************************************
    */

    /**
     * Loop invariant
     *
     * Calling this function within a loop VeriSol.Invariant(I) installs
     * I as a loop invariant. I should only refer to variables in scope
     * at the loop entry, and evaluated at the loop head. 
     * 
     * Using "Invariant" to avoid clash with a potential "invariant" keyword in Solidity
     * to directly support loop invariants https://github.com/ethereum/solidity/issues/6210
     */
    function Invariant(bool b) external pure;
     
    /**
     * Contract invariant
     * 
     * 
     * Calling this function within exactly one view function 
     * VeriSol.ContractInvariant(I) installs I  as a loop invariant
     * for the harness that calls public methods in the contract in a 
     * loop. See https://arxiv.org/abs/1812.08829 (Sec IV A)
     *
     * It is currently not inherited by derived contracts
     *
     * I should only refer to variables in global scope i.e. state variables.
     * 
     */
    function ContractInvariant(bool b) external pure;

    /**
     * Postconditions
     *
     * Calling this function within a function f VeriSol.Ensures(E) installs
     * E as a post condition of f. 
     */
    function Ensures(bool b) external pure;

    /**
     * Preconditions
     *
     * Calling this function within a function f VeriSol.Requires(E) installs
     * E as a pre condition of f. 
     */
    function Requires(bool b) external pure;
    

   /*
    ********************************************************************
    *  New functions for extending assertion language
    ********************************************************************
    */

    /**
     * A new in-built function that returns the sum of all values of a mapping
     * 
     */  
    function SumMapping(mapping (address => int256) storage a) external pure returns (int256);
    function SumMapping(mapping (address => uint256) storage a) external pure returns (uint256);

    /**
     * Function to refer to the state of an expression at the entry to a method 
     * 
     */  
    function Old(uint x) external pure returns (uint);
    function Old(int x) external pure returns (uint);
    function Old(address x) external pure returns (address);

    /**
     * Function to specify the upper bound on the modifies set 
     * 
     */  
    function Modifies(mapping (address => uint256) storage a, address[1] memory x) public pure;
    function Modifies(mapping (address => uint256) storage a, address[2] memory x) public pure;
    function Modifies(mapping (address => uint256) storage a, address[3] memory x) public pure;
    function Modifies(mapping (address => uint256) storage a, address[4] memory x) public pure;
    function Modifies(mapping (address => uint256) storage a, address[5] memory x) public pure;
    function Modifies(mapping (address => uint256) storage a, address[6] memory x) public pure;
    function Modifies(mapping (address => uint256) storage a, address[7] memory x) public pure;
    function Modifies(mapping (address => uint256) storage a, address[8] memory x) public pure;
    function Modifies(mapping (address => uint256) storage a, address[9] memory x) public pure;
    function Modifies(mapping (address => uint256) storage a, address[10] memory x) public pure;

}
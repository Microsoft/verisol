const createCsvWriter = require('csv-writer').createObjectCsvWriter;
const csvWriter = createCsvWriter({
    path: 'result.csv',
    header: [
      {id: 'method', title: 'Method'},
      {id: 'cel_gas', title: 'Celestial Gas'},
      {id: 'sol_gas', title: 'Solidity Gas'},
    ]
  });
const ERC20 = artifacts.require("ERC20");
const ERC20_cel = artifacts.require("ERC20_Cel");

contract("OpenZeppelin ERC20 evaluation", async accounts => {
    it("Evaluating Solidity and Celestial versions of ERC20 for gas consumption", async () => {

        let sender = accounts[0];
        let receiver = accounts[1];
        let mintAmt = 100;
        let approveAmt = 50;
        let increaseAmt = 10;
        let decreaseAmt = 10;
        let transferAmt = 10;

        // get data for original ERC20
      let instance = await ERC20.new();
      let deploymentReceipt = await web3.eth.getTransactionReceipt(instance.transactionHash);
      
      let tx = await instance._mint(sender, mintAmt, {from:sender});
      
      tx = await instance.approve(receiver, approveAmt, {from:sender});
      var approveGas = tx.receipt.gasUsed;

      tx = await instance.increaseAllowance(receiver, increaseAmt, {from:sender});
      var increaseAllowanceGas = tx.receipt.gasUsed;

      tx = await instance.transferFrom(sender, receiver, transferAmt, {from:receiver});
      var transferFromGas = tx.receipt.gasUsed;

      tx = await instance.decreaseAllowance(receiver, decreaseAmt, {from:sender});
      var decreaseAllowanceGas = tx.receipt.gasUsed;

      // get data for Celestial ERC20
      let instance_cel = await ERC20_cel.new();
      let deploymentReceipt_cel = await web3.eth.getTransactionReceipt(instance_cel.transactionHash);
      
      let tx_cel = await instance_cel.mint(sender, mintAmt, {from:sender});
      
      tx_cel = await instance_cel.approve(receiver, approveAmt, {from:sender});
      var approveGas_cel = tx_cel.receipt.gasUsed;

      tx_cel = await instance_cel.increaseAllowance(receiver, increaseAmt, {from:sender});
      var increaseAllowanceGas_cel = tx_cel.receipt.gasUsed;

      tx_cel = await instance_cel.transferFrom(sender, receiver, transferAmt, {from:receiver});
      var transferFromGas_cel = tx_cel.receipt.gasUsed;

      tx_cel = await instance_cel.decreaseAllowance(receiver, decreaseAmt, {from:sender});
      var decreaseAllowanceGas_cel = tx_cel.receipt.gasUsed;

      // dump results to csv
      const data = [
        {
            method: 'Approve',
            cel_gas: approveGas_cel,
            sol_gas: approveGas,
          },
        {
          method: 'IncreaseAllowance',
          cel_gas: increaseAllowanceGas_cel,
          sol_gas: increaseAllowanceGas,
        },
        {
            method: 'TransferFrom',
            cel_gas: transferFromGas_cel,
            sol_gas: transferFromGas,
          },
        {
            method: 'DecreaseAllowance',
            cel_gas: decreaseAllowanceGas_cel,
            sol_gas: decreaseAllowanceGas,
            }      
        ];
      
      csvWriter
        .writeRecords(data)
        .then(()=> console.log('The CSV file was written successfully'));
    })});
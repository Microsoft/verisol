const createCsvWriter = require('csv-writer').createObjectCsvWriter;
const csvWriter = createCsvWriter({
    path: 'result.csv',
    header: [
      {id: 'method', title: 'Method'},
      {id: 'cel_gas', title: 'Celestial Gas'},
      {id: 'sol_gas', title: 'Solidity Gas'},
    ]
  });
const Weth9 = artifacts.require("WETH9");
const Safe_Arith = artifacts.require("Safe_Arith");
const Weth9_cel = artifacts.require("WETH9_Cel");

contract("WrappedEther evaluation", async accounts => {
    it("Evaluating Solidity and Celestial versions of WrappedEther for gas consumption", async () => {

        let sender = accounts[0];
        let spender = accounts[1];
        let receiver = accounts[2];
        let depositAmt = 100;
        let withdrawAmt = 50;
        let approveAmt = 20;
        let transferAmt = 10;

        // get data for original WrappedEther
      let instance = await Weth9.new();
      let deploymentReceipt = await web3.eth.getTransactionReceipt(instance.transactionHash);
      var deploymentGas = parseInt(deploymentReceipt.gasUsed);

      let tx = await instance.deposit({from:sender, value:depositAmt});
      var depositGas = tx.receipt.gasUsed;

      tx = await instance.withdraw(withdrawAmt, {from:sender});
      var withdrawGas = tx.receipt.gasUsed;
      
      tx = await instance.approve(spender, approveAmt, {from:sender});
      var approveGas = tx.receipt.gasUsed;

      tx = await instance.transferFrom(sender, receiver, transferAmt, {from:spender});
      var transferFromGas = tx.receipt.gasUsed;

      // get data for Celestial WrappedEther
      let libraryInstance_cel = await Safe_Arith.new();
      let libraryReceipt_cel = await web3.eth.getTransactionReceipt(libraryInstance_cel.transactionHash);
      var libraryGas_cel = parseInt(libraryReceipt_cel.gasUsed);
      await Weth9_cel.link("Safe_Arith", libraryInstance_cel.address);
      let instance_cel = await Weth9_cel.new();
      let deploymentReceipt_cel = await web3.eth.getTransactionReceipt(instance_cel.transactionHash);
      var deploymentGas_cel = parseInt(deploymentReceipt_cel.gasUsed) + libraryGas_cel;

      let tx_cel = await instance_cel.deposit({from:sender, value:depositAmt});
      var depositGas_cel = tx_cel.receipt.gasUsed;

      tx_cel = await instance_cel.withdraw(withdrawAmt, {from:sender});
      var withdrawGas_cel = tx_cel.receipt.gasUsed;
      
      tx_cel = await instance_cel.approve(spender, approveAmt, {from:sender});
      var approveGas_cel = tx_cel.receipt.gasUsed;

      tx_cel = await instance_cel.transferFrom(sender, receiver, transferAmt, {from:spender});
      var transferFromGas_cel = tx_cel.receipt.gasUsed;

      // dump results to csv
      const data = [
        {
            method: 'Deployment',
            cel_gas: deploymentGas_cel,
            sol_gas: deploymentGas,
          },
        {
          method: 'Deposit',
          cel_gas: depositGas_cel,
          sol_gas: depositGas,
        },
        {
            method: 'Withdraw',
            cel_gas: withdrawGas_cel,
            sol_gas: withdrawGas,
          },
        {
            method: 'Approve',
            cel_gas: approveGas_cel,
            sol_gas: approveGas,
            },
        {
            method: 'TransferFrom',
            cel_gas: transferFromGas_cel,
            sol_gas: transferFromGas,
            }      
        ];
      
      csvWriter
        .writeRecords(data)
        .then(()=> console.log('The CSV file was written successfully'));
    })});
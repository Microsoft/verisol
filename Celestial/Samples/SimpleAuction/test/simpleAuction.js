const createCsvWriter = require('csv-writer').createObjectCsvWriter;
const csvWriter = createCsvWriter({
    path: 'result.csv',
    header: [
      {id: 'method', title: 'Method'},
      {id: 'cel_gas', title: 'Celestial Gas'},
      {id: 'sol_gas', title: 'Solidity Gas'},
    ]
  });
const SimpleAuction = artifacts.require("SimpleAuction");
const Safe_Arith = artifacts.require("Safe_Arith");
const SimpleAuction_cel = artifacts.require("SimpleAuction_Cel");

function sleep(ms) {
    return new Promise(resolve => setTimeout(resolve, ms));
}

contract("SimpleAuction evaluation", async accounts => {
    it("Evaluating Solidity and Celestial versions of SimpleAuction for gas consumption", async () => {

        let beneficiary = accounts[0];
        let biddingTime = 5;
        let bidder1 = accounts[1];
        let bidder1bid = 10;
        let bidder2 = accounts[2];
        let bidder2bid = 20;
        
        // get data for original SimpleAuction
      let instance = await SimpleAuction.new(biddingTime, beneficiary);
      let deploymentReceipt = await web3.eth.getTransactionReceipt(instance.transactionHash);
      var deploymentGas = parseInt(deploymentReceipt.gasUsed);  // retrieve gas needed for deployment
      
      let tx = await instance.bid({from:bidder1, value:bidder1bid});
      var bid1Gas = tx.receipt.gasUsed;

      tx = await instance.bid({from:bidder2, value:bidder2bid});
      var bid2Gas = tx.receipt.gasUsed;

      tx = await instance.withdraw({from:bidder1});
      var withdrawGas = tx.receipt.gasUsed;

      // doing this to ensure we go past the auctionEndTime
      await sleep(biddingTime*1000);

      tx = await instance.auctionEnd();
      var auctionEndGas = tx.receipt.gasUsed;

      // get data for Celestial SimpleAuction
      let instance_cel = await Safe_Arith.new();
      let libraryReceipt_cel = await web3.eth.getTransactionReceipt(instance_cel.transactionHash);
      var libraryGas_cel = parseInt(libraryReceipt_cel.gasUsed);
      await SimpleAuction_cel.link("Safe_Arith", instance_cel.address);
      instance_cel = await SimpleAuction_cel.new(biddingTime, beneficiary);
      let deploymentReceipt_cel = await web3.eth.getTransactionReceipt(instance_cel.transactionHash);
      var deploymentGas_cel = parseInt(deploymentReceipt_cel.gasUsed) + libraryGas_cel;  // retrieve gas needed for deployment
      
      let tx_cel = await instance_cel.bid({from:bidder1, value:bidder1bid});
      var bid1Gas_cel = tx_cel.receipt.gasUsed;

      tx_cel = await instance_cel.bid({from:bidder2, value:bidder2bid});
      var bid2Gas_cel = tx_cel.receipt.gasUsed;

      tx_cel = await instance_cel.withdraw({from:bidder1});
      var withdrawGas_cel = tx_cel.receipt.gasUsed;

      // doing this to ensure we go past the auctionEndTime
      await sleep(biddingTime*1000);

      tx_cel = await instance_cel.auctionEnd();
      var auctionEndGas_cel = tx_cel.receipt.gasUsed;

      // dump results to csv
      const data = [
        {
            method: 'Deployment',
            cel_gas: deploymentGas_cel,
            sol_gas: deploymentGas,
          },
        {
          method: 'Bid1',
          cel_gas: bid1Gas_cel,
          sol_gas: bid1Gas,
        },
        {
            method: 'Bid2',
            cel_gas: bid2Gas_cel,
            sol_gas: bid2Gas,
          },
        {
            method: 'Withdraw',
            cel_gas: withdrawGas_cel,
            sol_gas: withdrawGas,
            },
        {
            method: 'AuctionEnd',
            cel_gas: auctionEndGas_cel,
            sol_gas: auctionEndGas,
            }
      ];
      
      csvWriter
        .writeRecords(data)
        .then(()=> console.log('The CSV file was written successfully'));
    })});
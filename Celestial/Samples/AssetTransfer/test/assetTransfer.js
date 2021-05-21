const createCsvWriter = require('csv-writer').createObjectCsvWriter;
const csvWriter = createCsvWriter({
    path: 'result.csv',
    header: [
      {id: 'method', title: 'Method'},
      {id: 'cel_gas', title: 'Celestial Gas'},
      {id: 'sol_gas', title: 'Solidity Gas'},
    ]
  });
const MarketPlace = artifacts.require("MarketPlace");
const MarketPlace_cel = artifacts.require("MarketPlace_Cel");
const Safe_Arith = artifacts.require("Safe_Arith");

contract("AssetTransfer evaluation", async accounts => {
    it("Evaluating Solidity and Celestial versions of AssetTransfer for gas consumption", async () => {

        let buyer = accounts[0];
        let seller = accounts[1];
        let sellingPrice = 100;
        let incrementOffer = 50;

        // get data for original AssetTransfer
      let instance = await MarketPlace.new(seller, buyer);
      let deploymentReceipt = await web3.eth.getTransactionReceipt(instance.transactionHash);
      var deploymentGas = parseInt(deploymentReceipt.gasUsed);  // retrieve gas needed for deployment
      
      let tx = await instance.MakeOffer(sellingPrice, {from:seller});
      var makeOfferGas = tx.receipt.gasUsed;

      tx = await instance.ModifyOffer(true, incrementOffer, {from:seller});
      var modifyOfferGas = tx.receipt.gasUsed;

      tx = await instance.AcceptOffer({from:buyer, value:sellingPrice + incrementOffer});
      var acceptOfferGas = tx.receipt.gasUsed;

      tx = await instance.Accept({from:buyer});
      var acceptBuyerGas = tx.receipt.gasUsed;

      tx = await instance.Accept({from:seller});
      var acceptSellerGas = tx.receipt.gasUsed;

      tx = await instance.Withdraw({from:seller});
      var withdrawGas = tx.receipt.gasUsed;

      // get data for Celestial AssetTransfer
      let libraryInstance_cel = await Safe_Arith.new();
      let libraryReceipt_cel = await web3.eth.getTransactionReceipt(libraryInstance_cel.transactionHash);
      var libraryGas_cel = parseInt(libraryReceipt_cel.gasUsed);
      await MarketPlace_cel.link("Safe_Arith", libraryInstance_cel.address);
      let instance_cel = await MarketPlace_cel.new(seller, buyer);
      let deploymentReceipt_cel = await web3.eth.getTransactionReceipt(instance_cel.transactionHash);
      var deploymentGas_cel = parseInt(deploymentReceipt_cel.gasUsed) + libraryGas_cel;  // retrieve gas needed for deployment
      
      let tx_cel = await instance_cel.makeOffer(sellingPrice, {from:seller});
      var makeOfferGas_cel = tx_cel.receipt.gasUsed;

      tx_cel = await instance_cel.modifyOffer(true, incrementOffer, {from:seller});
      var modifyOfferGas_cel = tx_cel.receipt.gasUsed;

      tx_cel = await instance_cel.acceptOffer({from:buyer, value:sellingPrice + incrementOffer});
      var acceptOfferGas_cel = tx_cel.receipt.gasUsed;

      tx_cel = await instance_cel.accept({from:buyer});
      var acceptBuyerGas_cel = tx_cel.receipt.gasUsed;

      tx_cel = await instance_cel.accept({from:seller});
      var acceptSellerGas_cel = tx_cel.receipt.gasUsed;

      tx_cel = await instance_cel.withdraw({from:seller});
      var withdrawGas_cel = tx_cel.receipt.gasUsed;

      // dump results to csv
      const data = [
        {
            method: 'Deployment',
            cel_gas: deploymentGas_cel,
            sol_gas: deploymentGas,
          },
        {
          method: 'MakeOffer',
          cel_gas: makeOfferGas_cel,
          sol_gas: makeOfferGas,
        },
        {
            method: 'ModifyOffer',
            cel_gas: modifyOfferGas_cel,
            sol_gas: modifyOfferGas,
          },
        {
            method: 'AcceptOffer',
            cel_gas: acceptOfferGas_cel,
            sol_gas: acceptOfferGas,
            },
        {
            method: 'Accept (Buyer)',
            cel_gas: acceptBuyerGas_cel,
            sol_gas: acceptBuyerGas,
            },
        {
            method: 'Accept (Seller)',
            cel_gas: acceptSellerGas_cel,
            sol_gas: acceptSellerGas,
            },
        {
            method: 'Withdraw',
            cel_gas: withdrawGas_cel,
            sol_gas: withdrawGas,
            }
      ];
      
      csvWriter
        .writeRecords(data)
        .then(()=> console.log('The CSV file was written successfully'));
    })});
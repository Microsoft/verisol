const createCsvWriter = require('csv-writer').createObjectCsvWriter;
const csvWriter = createCsvWriter({
    path: 'result.csv',
    header: [
      {id: 'method', title: 'Method'},
      {id: 'cel_gas', title: 'Celestial Gas'},
      {id: 'sol_gas', title: 'Solidity Gas'},
    ]
  });
const MultiSig = artifacts.require("MultiSigWalletWithDailyLimit");
const Safe_Arith = artifacts.require("Safe_Arith");
const MultiSig_cel = artifacts.require("MultiSigWalletWithDailyLimit_Cel");

contract("Consensys MultiSig evaluation", async accounts => {
  it("Evaluating Solidity and Celestial versions of Consensys MultiSig for gas consumption", async () => {

      let owner1 = accounts[0];   
      let owner2 = accounts[1];    
      let owner3 = accounts[2];   
      let owner4 = accounts[3];
      let owner5 = accounts[4];

      let daily_limit = 10;

      let required = 3;
      let new_required = 2;

      let tx_destination = accounts[5];  // arbitrary destination address
      let tx_val = 100;

      // get data for original AssetTransfer
      console.log(".Creating and deploying MultiSig");
      let instance = await MultiSig.new([owner1, owner2, owner3], required, daily_limit); // constructor
      console.log("..Getting deployment receipt");
      let deploymentReceipt = await web3.eth.getTransactionReceipt(instance.transactionHash);         
      var deploymentGas = parseInt(deploymentReceipt.gasUsed);  // retrieve gas needed for deployment
      
      console.log(".Sending ether");
      instance.send(1000, {from:accounts[6]});

      console.log(".Adding Owner");
      let tx = await instance.addOwner(owner4);
      var addOwnerGas = tx.receipt.gasUsed;

      console.log(".Replace Owner");
      tx = await instance.replaceOwner(owner4, owner5);
      var replaceOwnerGas = tx.receipt.gasUsed;

      console.log(".Change Requirements");
      tx = await instance.changeRequirement(new_required);
      var changeRequirementGas = tx.receipt.gasUsed;

      console.log(".Remove Owner");
      tx = await instance.removeOwner(owner5);
      var removeOwnerGas = tx.receipt.gasUsed;

      console.log(".Submit Transaction");
      tx = await instance.submitTransaction(tx_destination, tx_val, {from:owner1});
      var txApproveGas = tx.receipt.gasUsed;

      console.log(".Confirm Transaction");
      tx = await instance.confirmTransaction(0, {from:owner2});
      txApproveGas += tx.receipt.gasUsed;

      // get data for Celestial MultiSig
      console.log(".Creating and Deploying Celestial MultiSig");
      let instance_cel = await Safe_Arith.new();
      let libraryReceipt_cel = await web3.eth.getTransactionReceipt(instance_cel.transactionHash);
      var libraryGas_cel = parseInt(libraryReceipt_cel.gasUsed);
      await MultiSig_cel.link("Safe_Arith", instance_cel.address);
      instance_cel = await MultiSig_cel.new(owner1, required, daily_limit);
      let deploymentReceipt_cel = await web3.eth.getTransactionReceipt(instance.transactionHash);
      let contractAddress_cel = instance_cel.address                    // address where the contract is deployed (needed because there is a weird modifier that says sender should be equal to address(this) -- private?)
      var deploymentGas_cel = parseInt(deploymentReceipt_cel.gasUsed) + libraryGas_cel;  // retrieve gas needed for deployment

      tx = await instance_cel.addOwner(owner2);
      deploymentGas_cel += tx.receipt.gasUsed;

      tx = await instance_cel.addOwner(owner3);
      deploymentGas_cel += tx.receipt.gasUsed;

      console.log(".Sending ether");
      instance_cel.send(1000, {from:accounts[6]});
      
      console.log(".Celestial Adding owner");
      tx = await instance_cel.addOwner(owner4);
      var addOwnerGas_cel = tx.receipt.gasUsed;

      console.log(".Celestial Replacing owner");
      tx = await instance_cel.replaceOwner(owner4, owner5);
      var replaceOwnerGas_cel = tx.receipt.gasUsed;

      console.log(".Celestial Changing Requirement");
      tx = await instance_cel.changeRequirement(new_required);
      var changeRequirementGas_cel = tx.receipt.gasUsed;

      console.log(".Celestial Removing owner");
      tx = await instance_cel.removeOwner(owner5);
      var removeOwnerGas_cel = tx.receipt.gasUsed;

      console.log(".Celestial Submitting Transaction");
      tx = await instance_cel.submitTransaction(tx_destination, tx_val, {from:owner1});
      var txApproveGas_cel = tx.receipt.gasUsed;

      tx = await instance_cel.confirmTransaction(0, {from:owner1});
      txApproveGas_cel += tx.receipt.gasUsed;

      console.log(".Celestial Confirming transaction");
      tx = await instance_cel.confirmTransaction(0, {from:owner2});
      txApproveGas_cel += tx.receipt.gasUsed;

      // dump results to csv
      const data = [
      {
          method: 'Deployment',
          cel_gas: deploymentGas_cel,
          sol_gas: deploymentGas,
      },
      {
        method: 'addOwner',
        cel_gas: addOwnerGas_cel,
        sol_gas: addOwnerGas,
      },
      {
          method: 'removeOwner',
          cel_gas: removeOwnerGas_cel,
          sol_gas: removeOwnerGas,
      },
      {
          method: 'replaceOwner',
          cel_gas: replaceOwnerGas_cel,
          sol_gas: replaceOwnerGas,
      },
      {
          method: 'changeRequirement',
          cel_gas: changeRequirementGas_cel,
          sol_gas: changeRequirementGas,
      },
      {
          method: 'Tx-approval',
          cel_gas: txApproveGas_cel,
          sol_gas: txApproveGas,
      }
    ];
    
    csvWriter
      .writeRecords(data)
      .then(()=> console.log('The CSV file was written successfully'));
  })});
# Experiments

This directory contains a subdirectory for each contract. Each subdirectory also includes a URL to the original Solidity contract. This contract has to be downloaded manually into the `contracts/` subdirectory for each experiment to run the gas comparision tests.

This directory also contains a `Data/` subdirectory which has the `.csv` file with the gas comparisions.

## Instructions to run the experiments

* `make compile experiment=<experiment_name>`

    Compiles the Celestial source for the specified experiment and generates the Solidity and F* versions.

* `make verify experiment=<experiment_name>`

    Verifies the generated F* code for the specified experiment.

* `make perf experiment=<experiment_name>`

    Generates a `.csv` file with gas comparisions for the specified experiment (except overview and binancecoin).

* `make <experiment_name>`

    Runs all the above targets for the specified experiment.

* `make all`

    Compiles, verifies and generates gas performance `.csv` file for all experiments.


### Experiments

- `asset_transfer`
- `etherdelta`
- `overview`
- `erc20`
- `wrapped_ether`
- `multisig`
- `simple_auction`
- `binancecoin`
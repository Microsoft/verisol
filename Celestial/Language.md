# Celestial

Celestial as a framework evolved a lot over the last 2 years from its inception as a completely new language to a framework that allows annotating Solidity contracts with expressive specifications and automatically discharging them. However, even after repeated iterations, some elements from Celestial's previous versions still remain. (These differences and restrictions are **purely** syntactic). This document explains these deviations from vanilla Solidity and also its specification language.

## Language

A contract in Celestial follows the below structure:
```
contract <contract_name>
{
    <field_variables>;

    invariant <invariant_name>
    {
        <expression : bool>
    }

    spec <spec_function_name> (<args>)
    {
        <expression : bool>
    }

    function <function_name> (<args>)
        <public/private>
        <pure/constant/view>
        <credit> <debit>
        pre <expression : bool>
        post <expression : bool>
        tx_reverts <expression : bool>
        modifies [<comma_separated_field_variable_names>]
        modifies_addresses [<comma_separated_addresses>]
        returns (<type> <variable_name>)
    {
        <contract_body_statements>;
        return;
    }
}
```

## Specification Language

Celestial's specification language contains the following constructs and annotations:
* `invariant`
* `spec` functions
* function specifications:
    * `credit`
    * `debit`
    * `pre`
    * `post`
    * `tx_reverts`
    * `modifies`
    * `modifies_addresses`
* `log` ghost variable
* quantifiers
* other macros

These elements are erased when generating Solidity. Note that all these constructs are 'pure'. None of the boolean expressions used in the specifications can modify the state. The following sections explain each of these constructs in detail.

### `invariant`
These are contract-level invariants. An `invariant`'s body is a single boolean expression over the contract field variables, including `balance`. All defined contract-invariants are conjugated and assumed in functions' pre-conditions are asserted in their post-conditions. We allow writing multiple `invariant` blocks for developer convenience and code readability.

### `spec` functions
These are pure functions whose bodies are just boolean expressions over their arguments. These functions allow for code reuse - repeating expressions can be wrapped in a `spec` function and called within `pre`, `post`, `tx_reverts` and other `spec` functions.

### function specifications

#### `credit` / `debit`
These are keywords that denote whether a contract function receives Ether, sends out Ether, both or neither. Functions that are not annotated with either `credit` or `debit` have a `new_balance == old_balance` clause added to its post-condition automatically. This check ensures that a contract does not send out Ether by accident. This forces developers to be cognizent of their contract's behviour, and explicitly add a `credit` or `debit` annotation to the function. Functions annotated with both `credit` and `debit` do not have any extra checks added automatically.

#### `pre`
This is the function's pre-condition, which is just a boolean expression over the function's arguments, field variables, `balance` and `spec` function invocations. For public functions, this is almost always trivial (`true`). For public functions in a contract that are never called by other functions in the same contract, for example, the pre-condiitons `address(this) != msg.sender` is sound. Private functions can have non-trivial pre-conditions. Of course, these pre-conditions have to be ensured before calling this private function, without which the verification would fail.

#### `post`
This is the function's post-condition, which is a boolean expression over the function's arguments, field variables, `log`, `balance`, the return variable, `spec` function invocations and also `new(<field_variable_name>)`. `new(<field_variable_name>)` denotes the updated value of the field variable (including `log` and `balance`) after the function is executed. The return variable is the name of the variable returned, denoted using Solidity's `returns (<type> <return_variable_name>)` clause.

#### `tx_reverts`
This is the function's revert clause which again is a boolean expression similar to `pre`. This specifies the conditions under which a function may revert (described in the paper in more detail).

#### `modifies`
This is a list of contract field variables that a function can modify. For all the field variables that are not in the `modifies` list, a `new_field_var == old_field_var` clause is added to the function's post-condition automatically. This ensures that a function cannot accidentally update a field variable. For functions that `emit` an event, the `log` variable should be added to the `modifies` clause explicitly. This prevents any unintended `emit`s caused due to that function.

#### `modifies_addresses`
This is a list of addresses that a function can modify. These are not address literals (of course, our blockchain model is unaware of the actual state of the Ethereum blockchain) but variables that represent contract addresses. For example, `Item.getPrice()` in the Overview example requires that `modifies_addresses[address(this)]` be specified so that the caller `SimpleMarket.buy()` is assured that its own state would not be modified on an invocation to `getPrice()` (due to a re-entrant call, for example).


### `log` ghost variable
The `log` is a specification-only variable (cannot be used in the contract body) that models the EVM log. An `emit(<event_name, <event_payloads>...)` in Celestial appends to the head of this log (a list) with a tuple `(<event_name>, <event_payloads>...)`. Post-conditions on `log` are written as follows: `new(log) == (<event_tuple1>)::(<event_tuple2>):: ... ::log`, where the head of the list is the last emitted event.

### quantifiers
Celestial also supports quantifiers in specification expressions as follows: `forall/exists (<type1> <var1>, <type2> <var2>, ...) (<boolean_expression_over_quantified_vars>)`. Note that excessive use of quantifiers can pollute Z3's VCs and may cause verification to break.

### macros
Celestial supports macros such as `sum_mapping` which sums all values contained in a `mapping (... => uint)`. It also supports checking membership of a key within a `mapping` using the `in` keyword (e.g. `... && (key in map_var) ...`). Soldity does not support these constructs, and hence are specification-only within Celestial. (Note, Solidity's `mapping()` type does not have the concept of a domain - all keys of that type are present in the mapping. However, we extend this mapping type in our F* model to allow writing more expressive specifications with respect to key membership in a mapping, say after adding or removing an element from the mapping). Celestial also provides a way to concisely represent the following predicate - A map has only a key `k` updated with value `v` while keeping the other key-value pairs same - using the syntax `new(map_var) == map_var[k => v]`.

The support for custom macros and functions is only bounded by F*'s expressiveness. It is easy to add new macros to Celestial to ease developer effort and to support more intricate specifications, akin to programming in F* directly, albeit in a more imperative style. These were just few macros that we found particularly useful during our analysis of popular smart contracts.

<hr />

## Deviations from Solidity

Celestial's flavour of Solidity has minor syntactic changes compared to vanilla Solidity and also has some restrictions. We are working on Celestial to support vanilla Solidity for a better developer experience. Until then, the following are the changes:
* `msg.value` is just `value` in Celestial
* `msg.sender` is just `sender` in Celestial
* `address(this).balance` is just `balance` in Celestial
* The zero address `address(0)` is `null` in Celestial
* Celestial defines macros `uint_max`, `int_max` and `int_min` that correspond to `~uint256(0)`, `(int256(~(uint256(1) << 255)))` and `(int256(uint256(1) << 255))` respectively (the maximum and minimum values for the `uint` and `int` types).
* Celestial requires `return`ing only at the end of a function and not in between.
* Functions in Celestial require explicitly writing a `return` at the end of the function body. 
* Celestial supports a type `inst_map<T>` where `T` is a contract type. This is just a `mapping (Item => bool)` in Solidity with additonal semantics during verification. The semantics are as follows:
    * Any addresses in an `inst_map<T>` are guaranteed to be of a contract type `T`, which in turn gives stronger guarantees on methods invoked on contract instances corresponding to this address.
    * This is ensured by allowing additions into the `inst_map` only via an `add()` function defined on the `inst_map` that requires `new T(...)` as an argument - such a restriction guarantees that the addresses are of type `T`.
    * Additions to the `inst_map` translate to setting the value in the `mapping` to `true`.
    * Lookups of an address from the `inst_map` returns the contract instance corresponding to that address if present (i.e. if the `mapping` value is `true`) or `address(0)` otherwise.
* `inst_map`s in specifications can only be used within `spec` functions and cannot be inlined in the `pre`, `post` and `tx_reverts` conditions.
* Local variables cannot be mutated within blocks.
* Explicit casting is not allowed, for e.g.: `Item(some_address)`

These syntactic changes can be resolved in the following ways:
* Exposing a lightweight library for the macros `uint_max`, ...
* Use Solidity's `mapping(T => bool)` itself in place of `inst_map<T>`s but provide an extra annotation (for the verification backend) to mark this `mapping()` to semantically equivalent to an `inst_map`. This annotation will be erased along with other specifications.
* Removing mandatory `return` statements is just a tweak in the prettyprinter.
* Field variables can be used instead of local variables if their mutability is required within blocks.

## Other remarks

* The Celestial -> F* and Celestial -> Solidity prettyprinters do lightweight typechecking. This will also be removed - F*'s typechecker suffices.
* F* verification may fail with an `unknown assertion failed` error if the contract is large. In such cases, the `z3rlimit` has to be increased.
* The specification eraser can sometimes reorder field variable declarations. However, this does not affect the semantics of the contract. This will be fixed in a future update.
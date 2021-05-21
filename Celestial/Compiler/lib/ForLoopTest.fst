module ForLoopTest


open FStar.Celestial
open FStar.Celestial.Effect
module CM = FStar.Celestial.ContractsMap
open FStar.Mul
module M = FStar.Celestial.Map
module L = FStar.List.Tot
module A = FStar.Celestial.Array

noeq type t_contract = {
  contract_sumCredits : int;
}

type contract_address = contract t_contract
let contract_live (c:contract_address) (bst:bstate) =
  c `CM.live_in` bst.cmap

let contract_get_sumCredits (c:contract_address)
: StEth int
  (fun st -> c `contract_live` st.current)
  (fun st0 r st1 ->
    st0 == st1 /\ r == (CM.sel c st0.current.cmap).contract_sumCredits)
= let contract_inst = get_contract c in
  contract_inst.contract_sumCredits

let contract_set_sumCredits (c:contract_address) (_sumCredits:int)
: StEth unit
  (fun st -> c `contract_live` st.current
              /\ _sumCredits >= 0 /\ _sumCredits <= uint_max)
  (fun st0 _ st1 ->
    modifies_cmap_only (Set.singleton c) st0.current st1.current /\
    contract_live c st1.current /\
    (let instance0 = CM.sel c st0.current.cmap in
     let instance1 = CM.sel c st1.current.cmap in
    instance1 == { instance0 with contract_sumCredits = _sumCredits }))
= let contract_inst = get_contract c in
  let contract_inst = { contract_inst with contract_sumCredits = _sumCredits } in
  set_contract c contract_inst

(*For combinator*)
/// I initially thought of writing the `for` combinator in a separate F* module, but one of
///   its arguments `self` has to be of type `contract t_contract` which is contract specific
///   so decided to generate it once for each contract (I guess we can have an implicit Type
///   argument as well, but wanted to try out the easiest possible thing for now)
val for:
    start    :uint ->
    finish   :uint{finish >= start} ->
    freverts :(bstate -> uint -> Type0) ->             // Reverts condition for the function  `f`
    fpost    :(bstate -> bstate -> uint -> Type0) ->   // Post condition for the function `f` (assuming a trivial pre-condition for now. can have a `fpre` argument too)
    linv     :(bstate -> uint -> Type0) ->             // Loop invariant
    lreverts :(bstate -> uint -> Type0) ->             // Reverts condition for the combinator itself
    self     :contract_address ->
    sender   :address ->
    value    :uint ->
    f        :(self:contract_address -> sender:address -> value:uint -> i:uint{i >= start /\ i < finish} ->
              Eth unit (fun bst -> contract_live self bst /\ (linv bst i))
                       (fun bst -> freverts bst i)
                       (fun bst0 x bst1 -> (contract_live self bst1) /\ (fpost bst0 bst1 i) /\ (linv bst1 i))) ->
              // f is the body of the loop. Not handling local variables for now
    Eth unit
    (fun bst -> (contract_live self bst) /\ (linv bst start))            // /\ fpre bst
    
    (fun bst -> lreverts bst start)                                      // freverts bst0 start || freverts bst1 start+1 || ... freverts bstn finish
    (fun bst0 x bst1 -> (contract_live self bst1) /\ (linv bst1 finish))

let rec for start finish freverts fpost linv lreverts self sender value f =
    if start = finish then ()
    else begin
        f self sender value start;
        for (start + 1) finish freverts fpost linv lreverts self sender value f
    end
(*End For combinator*)

/// the reverts and post condition for `foo`
let foo_reverts (bst:bstate) (i:uint) : Type0 = False
let foo_post (bst0:bstate) (bst1:bstate) (i:uint) : Type0 = True

/// foo is the body of the for-loop
///   the body of the loop just updates the value of the field variable `sumCredits` to the loop iterator 'i'
let foo (self:contract_address) (sender:address) (value:uint) (i:uint) : Eth unit
(fun bst -> contract_live self bst)
(fun bst -> foo_reverts bst i)
(fun bst0 _ bst1 -> (contract_live self bst1) /\ (foo_post bst0 bst1 i))
= contract_set_sumCredits self i

/// bar is a method that has a for-loop
let bar (self:contract_address) (sender:address) (value:uint) : Eth unit
(fun bst -> contract_live self bst)
(fun bst -> foo_reverts bst 0)
(fun _ _ _ -> True)
= for 0 10 foo_reverts foo_post (fun _ _ -> True) (fun bst i -> foo_reverts bst i) self sender 0 foo

/// The issue is that, writing a `for` combinator in the style of KreMLin C loops requires passing
///   in the `reverts` condition (`lreverts`) for the combinator itself as an argument to the combinator. 
///   And hence F* is not able to verify it under the Eth effect since it doesn't know what `lreverts` is
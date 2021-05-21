module FStar.Celestial.ContractsMap

open FStar.Celestial
module S = FStar.Set
module M = FStar.Map

noeq type cvalue = {
  cvalue_a    : Type0;
  cvalue_inst : cvalue_a;
}

noeq
type cmap = {
  ctr : pos;
  contracts : (m:M.t  address cvalue{
    (ctr > 0) /\
    (~ (m `M.contains` 0)) /\
    (forall (i: address).{:pattern M.contains m i} m `M.contains` i ==> i < ctr)
  });
}

let live_in #a c m =
  m.contracts `M.contains` c /\
  (M.sel m.contracts c).cvalue_a == a

let not_in #_ c m = ~ (M.contains m.contracts c)


let sel #_ c m = (M.sel m.contracts c).cvalue_inst

let upd #a c m x =
  let ct = M.sel m.contracts c in
  let ct = { ct with cvalue_inst = x } in
  { m with contracts = M.upd m.contracts c ct }

let create #a m x =
  let cval = { cvalue_a = a; cvalue_inst = x } in 
  m.ctr, { m with ctr = m.ctr + 1;
                  contracts = M.upd m.contracts m.ctr cval }

let distinct_addrs_distinct_types #_ #_ _ _ _ = ()
let distinct_addrs_unused #_ #_ _ _ _ = ()
let live_in_not_in #_ _ _ = ()
let live_not_null #_ _ _ = ()
let upd_modifies #_ _ _ _ = ()
let sel_upd #_ _ _ _ = ()
let create_modifies #_ _ _ = ()

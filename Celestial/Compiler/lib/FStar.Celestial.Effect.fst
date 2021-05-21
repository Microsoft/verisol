module FStar.Celestial.Effect

open FStar.Celestial
module M = FStar.Map
module CM = FStar.Celestial.ContractsMap

noeq
type bstate = {
  cmap : CM.cmap;
  balances : M.t address uint;
  log  : log
}

noeq
type state = {
  tx_begin : bstate;
  current  : bstate;
}


(* Pure functions to read and modify state *)

let pure_commit_tx (st:state) : state = { st with tx_begin = st.current }
let pure_revert_tx (st:state) : state = { st with current = st.tx_begin }

let pure_get_cmap (st:state) = st.current.cmap
let pure_get_log (st:state) = st.current.log

let pure_update_cmap (st:state) (m:CM.cmap) =
  { st with current = { st.current with cmap = m } }
let pure_update_log (st:state) (l:log) =
  { st with current = { st.current with log = l } }

let pure_get_log_bst (bst:bstate) = bst.log
let pure_update_log_bst (bst:bstate) (l:log) = 
  { bst with log = l }

let pure_get_balance_st (addr:address) (st:state) = M.sel st.current.balances addr
let pure_set_balance_st (addr:address) (n:uint) (st:state) =
  { st with current = { st.current with
                        balances = M.upd (st.current.balances) addr n } }

let pure_get_balance_bst (addr:address) (bst:bstate) = M.sel bst.balances addr
let pure_set_balance_bst (addr:address) (n:uint) (bst:bstate) =
  { bst with balances = M.upd bst.balances addr n }


(* Effect definition *)

new_effect STETH = STATE_h state

effect StEth (a:Type) (pre:state -> Type0) (post:(s:state{pre s}) -> a -> state -> Type0) =
  STETH a (fun p s0 -> pre s0 /\ (forall (x:a) (s1:state). post s0 x s1 ==> p x s1))

unfold
let lift_pure_steth (a:Type) (wp:pure_wp a) (p:a -> state -> Type0) (s0:state) = wp (fun x -> p x s0)

sub_effect PURE ~> STETH = lift_pure_steth

type result (a:Type) =
  | V : x:a -> result a
  | E : s:string -> result a

type pre_t = state -> Type0
type post_t (a:Type) = result a -> state -> Type0
type wp_t (a:Type) = post_t a -> pre_t

unfold
let return_wp (a:Type) (x:a) : wp_t a =
  fun p s -> p (V x) s

unfold
let bind_wp (_:range) (a:Type) (b:Type) (wp1:wp_t a) (wp2:a -> wp_t b) : wp_t b =
  fun p st0 -> wp1 (fun x st1 ->
    match x with
    | V x -> (wp2 x) p st1
    | E x -> p (E x) st1) st0

unfold
let if_then_else_wp (a:Type) (p:Type0) (wp_then wp_else: wp_t a) : wp_t a =
  fun post st0 -> l_ITE p (wp_then post st0) (wp_else post st0)

unfold
let ite_wp (a: Type) (wp:wp_t a) : wp_t a =
  fun p st0 ->
  forall (k:post_t a).
    (forall (x:result a) (st1:state). {:pattern (guard_free (k x st1))} p x st1 ==> k x st1) ==> wp k st0

unfold
let stronger (a:Type) (wp1 wp2:wp_t a) = forall (p:post_t a) (st:state). wp1 p st ==> wp2 p st

unfold
let close_wp (a b:Type) (wp:(b -> GTot (wp_t a))) : wp_t a =
  fun p st0 -> forall (b:b). wp b p st0

unfold
let trivial (a:Type) (wp:wp_t a) = forall st0. wp (fun _ _ -> True) st0

new_effect {
  ETH : a:Type -> wp:wp_t a -> Effect
  with
    return_wp = return_wp;
    bind_wp = bind_wp;
    if_then_else = if_then_else_wp;
    ite_wp = ite_wp;
    stronger = stronger;
    close_wp = close_wp;
    trivial = trivial
}

unfold
let lift_steth_eth (a:Type) (wp:st_wp_h state a) : wp_t a =
  fun p st -> wp (fun x st1 -> p (V x) st1) st

sub_effect STETH ~> ETH = lift_steth_eth


(*** These are primitive effect actions. ***)
assume val get (_:unit)               : STETH state (fun p st -> p st st)
assume val put (st:state)             : STETH unit  (fun p _ -> p () st)
assume val raise (#a:Type) (s:string) : ETH a       (fun p st -> p (E s) st)


(* Begin, commit, getters, setters *)

let begin_transaction (_:unit) : STETH unit (fun p st -> p () st) = ()

let commit_transaction (_:unit)
: STETH unit (fun p st -> p () (pure_commit_tx st))
= let st = get () in
  put ({ st with tx_begin = st.current })

let revert (#a:Type) (s:string)
: ETH a (fun p st -> p (E s) (pure_revert_tx st))
= let st = get () in
  put ({ st with current = st.tx_begin });
  raise s

let create_contract (#a:Type0) (x:a)
: STETH (contract a) (fun p st ->
    let c, m = CM.create st.current.cmap x in
    p c (pure_update_cmap st m))
= let st = get () in
  let c, m = CM.create st.current.cmap x in
  put (pure_update_cmap st m);
  c

let get_contract (#a:Type0) (c:contract a)
: STETH a (fun p st ->
    c `CM.live_in` st.current.cmap /\
    (let c = CM.sel c st.current.cmap in
     p c st))
= let st = get () in
  CM.sel c st.current.cmap

let get_balance (addr:address)
: STETH nat
  (fun p st -> p (pure_get_balance_st addr st) st)
= let st = get () in
  pure_get_balance_st addr st

let set_contract (#a:Type0) (c:contract a) (x:a)
: STETH unit (fun p st ->
    c `CM.live_in` st.current.cmap /\
    p () (pure_update_cmap st (CM.upd c st.current.cmap x)))
= let st = get () in
  let m = CM.upd c st.current.cmap x in
  put (pure_update_cmap st m)

let set_balance (addr:address) (n:nat{n <= uint_max})
: STETH unit (fun p st -> p () (pure_set_balance_st addr n st))
= let st = get () in
  put (pure_set_balance_st addr n st)

let add_event (ev:event)
: STETH unit
  (fun p st -> p () (pure_update_log st (ev::(pure_get_log st))))
= let st = get () in 
  put (pure_update_log st (ev::(pure_get_log st)))

effect Eth0
  (a:Type)
  (pre:state -> Type0)
  (post:(st:state{pre st}) -> result a -> state -> Type0)
= ETH a
  (fun p st -> pre st /\ (forall (x:result a) (st1:state). post st x st1 ==> p x st1))

effect Eth
  (a:Type)
  (pre:bstate -> Type0)
  (revert:(bst:bstate{pre bst}) -> Type0)
  (post:(bst:bstate{pre bst /\ (~ (revert bst))}) -> a -> bstate -> Type0)
= Eth0 a
  (fun st -> pre st.current)
  (fun st0 r st1 ->
    (E? r <==> revert st0.current) /\
    (V? r ==> post st0.current (V?.x r) st1.current))

effect Eth1
  (a:Type)
  (pre:bstate -> Type0)
  (revert:(bst:bstate{pre bst}) -> Type0)
  (post:(bst:bstate{pre bst /\ (~ (revert bst))}) -> a -> bstate -> Type0)
= Eth0 a
  (fun st -> pre st.current)
  (fun st0 r st1 ->
    (revert st0.current ==> E? r) /\
    (V? r ==> post st0.current (V?.x r) st1.current))

let modifies_cmap_only (s:Set.set address) (bst0 bst1:bstate) =
  CM.modifies_addrs s bst0.cmap bst1.cmap /\
  bst0.balances == bst1.balances /\
  bst0.log == bst1.log

let modifies_cmap_and_log_only (s:Set.set address) (l: list event) (bst0 bst1:bstate) =
  CM.modifies_addrs s bst0.cmap bst1.cmap /\
  bst0.balances == bst1.balances /\
  bst1.log == l@bst0.log
  
let modifies_balances (s:Set.set address) (m0 m1: M.t address (n:nat{n <= uint_max})) =
  (forall (a: address).
    (~ (Set.mem a s)) ==> M.sel m0 a ==  M.sel m1 a)

let modifies_cmap_log_balances (cs:Set.set address) (l: list event) (bs:Set.set address) (bst0 bst1:bstate) =
  CM.modifies_addrs cs bst0.cmap bst1.cmap /\
  modifies_balances bs bst0.balances bst1.balances /\
  bst1.log == l@bst0.log

let modifies_log_balances_only (l: list event) (bs:Set.set address) (bst0 bst1:bstate) =
  CM.modifies_addrs (Set.empty) bst0.cmap bst1.cmap /\
  modifies_balances bs bst0.balances bst1.balances /\
  bst1.log == l@bst0.log


(* emit and send definitions *)

let emit (#a:Type0) (evn:string) (payload:a)
: STETH unit
  (fun p st -> p () (pure_update_log st ((mk_event null evn payload)::(pure_get_log st))))
= add_event (mk_event null evn payload)

// assume val call_value (sender:address) (recipient:address) (amount:uint)
// : Eth bool
//   (fun _ -> True)
//   (fun _ -> False)
//   (fun st0 _ st1 -> st0 == st1)

let transfer (#a:Type0) (c_addr:contract a) (to:address) (amount:uint)
: Eth1 unit
  (fun bst -> c_addr `CM.live_in` bst.cmap /\ (pure_get_balance_bst c_addr bst) >= amount)
  (fun bst -> False)
  (fun bst0 r bst1 ->
    (bst1.log == ((mk_event to eTransfer amount)::[])@(bst0.log)) /\
    (bst1.cmap == bst0.cmap) /\
    (
      if c_addr <> to then
        modifies_cmap_log_balances (Set.empty) ((mk_event to eTransfer amount)::[]) (Set.union (Set.singleton c_addr) (Set.singleton to)) bst0 bst1 /\
        (let b = pure_get_balance_bst c_addr bst0 in
        let b_to = pure_get_balance_bst to bst0 in
        let b_to_updated = (if b_to + amount > uint_max then (b_to + amount - uint_max) else (b_to + amount)) in
        M.equal bst1.balances (M.upd (M.upd bst0.balances c_addr (b - amount)) to (b_to_updated)))
      else
        bst1.balances == bst0.balances
    )
  )
= let b = get_balance c_addr in
  let _ = add_event (mk_event to eTransfer amount) in
  let b_to = get_balance to in
  if c_addr <> to then begin
    set_balance c_addr (b - amount);
    (if b_to + amount > uint_max then set_balance to (b_to + amount - uint_max)
    else set_balance to (b_to + amount))
  end
  else
    ()

/// Models a call made to an external/unknown entity
/// caller's state remains the same since reentrancy is disallowed
/// caller's balance may increase/decrease due to selfdestruct() and overflow
assume val unknown_call : (#a:Type0) -> (self:contract a) -> bytes -> Eth (bool * bytes)
(fun bst -> self `CM.live_in` bst.cmap)
(fun bst -> False)
(fun bst0 _ bst1 ->
  (self `CM.live_in` bst1.cmap)
  /\ (CM.sel self bst1.cmap) == (CM.sel self bst0.cmap)
)


assume val call_uint : (#a:Type0) -> (self:contract a) -> (b:bytes) -> Eth1 uint
(fun bst -> self `CM.live_in` bst.cmap)
(fun bst -> False)
(fun bst0 _ bst1 ->
  (self `CM.live_in` bst1.cmap)
  /\ (CM.sel self bst1.cmap) == (CM.sel self bst0.cmap)
)

assume val call_bool : (#a:Type0) -> (self:contract a) -> (b:bytes) -> Eth1 bool
(fun bst -> self `CM.live_in` bst.cmap)
(fun bst -> False)
(fun bst0 _ bst1 ->
  (self `CM.live_in` bst1.cmap)
  /\ (CM.sel self bst1.cmap) == (CM.sel self bst0.cmap)
)

assume val addmod : (x:uint) -> (y:uint) -> (k:uint) -> Eth uint
(fun _ -> True)
(fun _ -> k == 0)
(fun _ r _ -> r == (x + y) % k)

assume val mulmod : (x:uint) -> (y:uint) -> (k:uint) -> Eth uint
(fun _ -> True)
(fun _ -> k == 0)
(fun _ r _ -> r == (op_Multiply x y) % k)

let safe_mul (a:uint) (b:uint) : Eth uint
(fun _ -> True)
(fun _ -> (op_Multiply a b) > uint_max)
(fun bst0 r bst1 ->
  bst0 == bst1
  /\ r == (_mul a b)
)
= if (op_Multiply a b) > uint_max then revert ""
  else (_mul a b)
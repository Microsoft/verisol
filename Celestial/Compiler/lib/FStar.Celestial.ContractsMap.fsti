module FStar.Celestial.ContractsMap

open FStar.Celestial
module S = FStar.Set

/// This library provides an abstract type of contractsmap which models 
/// the logical mapping from addresses to contract instances in a blockchain. 

val cmap : Type u#1

let addr_of (#a:Type0) (c:contract a) : address = c

val live_in (#a:Type0) (c:contract a) (m:cmap) : prop

val not_in (#a:Type0) (c:contract a) (m:cmap) : prop

unfold
let fresh (#a:Type0) (c:contract a) (m0 m1:cmap) : prop =
  c `not_in` m0 /\ c `live_in` m1

val sel (#a:Type0) (c:contract a) (m:cmap{c `live_in` m}) : a

val upd (#a:Type0) (c:contract a) (m:cmap{c `live_in` m}) (x:a) : cmap

val create (#a:Type0) (m:cmap) (x:a) : contract a & cmap

let modifies_addrs (s:S.set address) (m0 m1:cmap) =
  (forall (a:Type0) (c:contract a).
    c `live_in` m0 ==> c `live_in` m1) /\
  (forall (a:Type0) (c:contract a).
    ((~ (S.mem c s)) /\ c `live_in` m0) ==> sel c m0 == sel c m1)

val distinct_addrs_distinct_types (#a #b:Type0) (m:cmap) (ca:contract a) (cb:contract b)
: Lemma
  (requires a =!= b /\ ca `live_in` m /\ cb `live_in` m)
  (ensures ~ (eq2 #address ca cb))
  [SMTPat (ca `live_in` m); SMTPat (cb `live_in` m)]

val distinct_addrs_unused (#a #b:Type0) (c1:contract a) (c2:contract b) (m:cmap)
: Lemma
  (requires c1 `not_in` m /\ ~ (c2 `not_in` m))
  (ensures addr_of c1 =!= addr_of c2 /\ (~ (c1 === c2)))
  [SMTPat (c1 `not_in` m); SMTPat (c2 `not_in` m)]

val live_in_not_in (#a:Type0) (c:contract a) (m:cmap)
: Lemma
  (requires c `live_in` m)
  (ensures (~ (c `not_in` m)))
  [SMTPatOr [[SMTPat (c `live_in` m)]; [SMTPat (c `not_in` m)]]]

val live_not_null (#a:Type0) (m:cmap) (c:contract a)
: Lemma
  (requires c `live_in` m)
  (ensures c <> null)
  [SMTPat (c `live_in` m)]

val upd_modifies (#a:Type0) (c:contract a) (m:cmap{c `live_in` m}) (x:a)
: Lemma
  (modifies_addrs (Set.singleton c) m (upd c m x))
  [SMTPat (upd c m x)]

val sel_upd (#a:Type0) (c:contract a) (m:cmap{c `live_in` m}) (x:a)
: Lemma
  (sel c (upd c m x) == x)
  [SMTPat (sel c (upd c m x))]

val create_modifies (#a:Type0) (m:cmap) (x:a)
: Lemma
  (let c, m' = create m x in
   c =!= null /\
   fresh c m m' /\
   modifies_addrs S.empty m m' /\
   sel c m' == x)
  [SMTPat (create m x)]

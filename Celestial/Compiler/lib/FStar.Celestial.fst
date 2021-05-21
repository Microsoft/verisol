module FStar.Celestial

module M = FStar.Celestial.Map
open FStar.Mul

let address = nat
let null:address = 0
assume type bytes
assume type bytes4
assume type bytes20

let contract (a:Type0) : Type0 = address

let int_max : int = 57896044618658097711785492504343953926634992332820282019728792003956564819967
let int_min : int = -1 * (57896044618658097711785492504343953926634992332820282019728792003956564819968)
let uint_max : int = 115792089237316195423570985008687907853269984665640564039457584007913129639935
let uint8_max : int = 255

type uint = n:nat{n <= uint_max}
type int = n:int{n >= int_min /\ n <= int_max}
type uint8 = n:nat{n <= uint8_max}
let bytes32 = uint

noeq
type event = {
  payload_typ : Type0;
  receiver    : address;
  name        : string;
  payload     : payload_typ
}

let mk_event (#a:Type0) (to:address) (evn:string) (payload:a) = {
  payload_typ = a;
  receiver = to;
  name = evn;
  payload = payload
}

type log = list event

let eTransfer : string = "eTransfer"

noeq
type block = {
  coinbase   : (a:address{a <> null});
  difficulty : uint;
  gaslimit   : uint;
  number     : (n:uint{n <> 0});
  timestamp  : (n:uint{n <> 0})
}

noeq
type tx = {
  origin   : (a:address{a <> null});
  gasprice : uint
}

let _add (a b : uint) : Pure uint
(requires a + b <= uint_max)
(ensures fun r -> r == a + b)
= a + b

let _sub (a b : uint) : Pure uint
(requires a >= b)
(ensures fun r -> r == a - b)
= a - b

let _mul (a b : uint) : Pure uint
(requires a * b <= uint_max)
(ensures fun r -> r == a * b)
= a * b

let _div (a:uint) (b:uint{b <> 0}) : Pure uint
(requires True)
(ensures fun r -> r == op_Division a b)
= op_Division a b

let fold_add_fun (#key:eqtype) : key -> uint -> Prims.int -> Prims.int = fun _ v1 v2 -> v1 + v2

let sum_mapping_aux (#key:eqtype) (#c:M.cmp key) (m:M.t key uint c) (n:Prims.int) : Prims.int =
  M.fold fold_add_fun m n

let rec sum_mapping_acc (#key:eqtype) (#c:M.cmp key) (m:M.t key uint c) (n1 n2:Prims.int)
: Lemma
  (ensures sum_mapping_aux m (n1 + n2) = n1 + sum_mapping_aux m n2)
  (decreases (M.size m))
= if M.size m = 0 then ()
  else begin
    M.non_zero_size_choose m;
    let Some (k, v) = M.choose m in
    let rest_m = M.delete m k in
    M.contains_choose m;
    sum_mapping_acc rest_m n1 (v+n2)
  end

let sum_mapping_rest (#key:eqtype) (#c:M.cmp key) (m:M.t key uint c) (n:Prims.int)
: Lemma
  (requires M.size m > 0)
  (ensures sum_mapping_aux m n ==
    (M.non_zero_size_choose m;
     let Some (k, v) = M.choose m in
     let rest_m = M.delete m k in
     v + sum_mapping_aux rest_m n))
= M.non_zero_size_choose m;
  let Some (k, v) = M.choose m in
  let rest_m = M.delete m k in
  sum_mapping_acc rest_m v n

let rec sum_mapping_aux_upd (#key:eqtype) (#c:M.cmp key) (m:M.t key uint c) (k:key) (v:uint) (n:Prims.int)
: Lemma
  (requires ~ (M.contains m k))
  (ensures sum_mapping_aux (M.upd m k v) n == v + sum_mapping_aux m n)
  (decreases (M.size m))
= let m1 = M.upd m k v in
  M.size_upd m k v;
  M.non_zero_size_choose m1;
  let Some (k', v') = M.choose m1 in
  let rest_m1 = M.delete m1 k' in

  if k' = k then begin
    sum_mapping_rest m1 n;
    M.delete_upd_cancel_out m k v
  end
  else begin
    M.choose_not_already_exist m k v;
    M.non_zero_size_choose m;
    let Some (k'', v'') = M.choose m in
    let rest_m = M.delete m k'' in
    M.choose_after_update m k v;
    M.choose_commute_up_del m k v;
    M.contains_choose m;
    sum_mapping_aux_upd rest_m k v n;
    sum_mapping_rest m1 n;
    sum_mapping_rest m n
  end

let sum_mapping (#k:eqtype) (#f:M.cmp k) (m:M.t k uint f) = sum_mapping_aux #k #f m 0

let sum_mapping_emp (#key:eqtype) (#f:M.cmp key) (m:(M.t key uint f){M.def_of m == 0})
: Lemma
  (requires M.size m == 0)
  (ensures sum_mapping m == 0)
  [SMTPat (M.domain m); SMTPat (sum_mapping m)]
= ()

let sum_mapping_const (#key:eqtype) (#f:M.cmp key)
: Lemma
  (ensures (sum_mapping (M.const #key #uint #f 0)) == 0)
  [SMTPat (M.const #key #uint #f)]
= ()

let rec sum_mapping_gt_val (#key:eqtype) (#f:M.cmp key)
  (m:(M.t key uint f){M.def_of m == 0}) (k:key)
: Lemma
  (requires (forall (i:key{M.contains m i}). M.sel m i >= 0))
  (ensures sum_mapping m >= M.sel m k)
  (decreases (M.size m))
  [SMTPat (sum_mapping m); SMTPat (m `M.contains` k)]
= if M.size m = 0 then M.empty_contains m k
  else begin
    M.non_zero_size_choose m;
    let Some (k', v) = M.choose m in
    let rest_m = M.delete m k' in
    M.contains_choose m;
    sum_mapping_rest m 0;
    sum_mapping_gt_val rest_m k
  end

let rec sum_mapping_upd (#key:eqtype) (#f:M.cmp key)
  (m:(M.t key uint f){M.def_of m == 0}) (k:key) (v:uint)
: Lemma
  (requires True)
  (ensures
    (let m1 = M.upd m k v in
     let prev_n = M.sel m k in
     eq2 #Prims.int (sum_mapping m1) (sum_mapping m - prev_n + v)))
  (decreases (M.size m))
  [SMTPat (sum_mapping (M.upd m k v))]
= let m1 = M.upd m k v in
  M.size_upd m k v;

  if M.contains m k then begin
    M.contains_size m k;
    M.non_zero_size_choose m;
    M.non_zero_size_choose m1;

    let Some (k', v') = M.choose m in
    let Some (k'', v'') = M.choose m1 in

    let rest_m = M.delete m k' in
    let rest_m1 = M.delete m1 k'' in

    M.contains_choose m;

    
    M.choose_after_update m k v;

    if k' = k then begin
      sum_mapping_rest m 0;
      sum_mapping_rest m1 0
    end
    else begin
     
      M.choose_commute_up_del m k v;
      sum_mapping_rest m 0;
      sum_mapping_rest m1 0;
      sum_mapping_upd rest_m k v
    end
  end
  else sum_mapping_aux_upd m k v 0

let lt = fun x y -> x <= y


(*** It is the string comparison function, e.g. the strcmp in OCaml: https://caml.inria.fr/pub/docs/manual-ocaml/libref/Stdlib.html#VALcompare
which behaves like a total order ***)
assume val strcmp : f:(string -> string -> bool){M.total_order string f}

(* ABI Enconding and Decoding functions *)
assume val abi_encode              : (#a:Type0) -> a -> bytes
assume val abi_encodePacked        : (#a:Type0) -> a -> bytes
assume val abi_encodeWithSelector  : (#a:Type0) -> bytes4 -> a -> bytes
assume val abi_encodeWithSignature : (#a:Type0) -> a -> bytes

(* Precompiles and other pure functions *)
assume val keccak256 : bytes -> bytes32
assume val sha256    : bytes -> bytes32 // precompile
assume val ripemd160 : bytes -> bytes20 // precompile
assume val ecrecover : bytes32 -> uint8 -> bytes32 -> bytes32 -> address // precompile
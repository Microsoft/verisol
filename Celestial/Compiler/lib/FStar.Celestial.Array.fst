(* This module implements Solidity Array semantics.*)

module FStar.Celestial.Array
open FStar.Celestial
module List = FStar.List.Tot

noeq
type array (a:Type u#a) = 
{
    arr : (s:Seq.seq a{Seq.length s <= uint_max});
    def : a
}

let create_empty #a v
= {
    arr = Seq.empty #a;
    def = v
}

let length #a s
= Seq.length s.arr

let update #a s n v
= {
    arr = Seq.upd s.arr n v;
    def = s.def
}

let push #a s v
= {
    arr = Seq.snoc s.arr v;
    def = s.def
}

let select #a s n
= Seq.index s.arr n

let delete #a s n
= update s n s.def

let def_of #a s
= s.def

let pop #a s
= {
    arr = Seq.slice s.arr 0 (Seq.length s.arr - 1);
    def = s.def
}

let lemma_len_create #_ _ = ()
let lemma_len_update1 #_ _ _ _ = ()
let lemma_len_push #_ _ _ = ()
let lemma_len_pop #_ _ = ()
let lemma_len_del #_ _ _ = ()
let lemma_select_update1 #_ _ _ _ = ()
let lemma_select_update2 #_ _ _ _ _ = ()
let lemma_select_push #_ _ _ = ()
let lemma_select_push2 #_ _ _ _ = ()
let lemma_select_pop #_ _ _ = ()
let lemma_select_delete1 #_ _ _ = ()
let lemma_select_delete2 #_ _ _ _ = ()
let lemma_default_update #_ _ _ _ = ()
let lemma_default_push #_ _ _ = ()
let lemma_default_pop #_ _ = ()
let lemma_default_delete #_ _ _ = ()
let lemma_pop_empty #_ _ = ()

let equal #a s1 s2
= (length s1 = length s2
   /\ (forall (i:nat{i < length s1}).{:pattern (select s1 i); (select s2 i)} (select s1 i == select s2 i))
   /\ (s1.def == s2.def))

let lemma_eq_intro #_ _ _ = ()
let lemma_eq_refl #_ _ _ = ()
let lemma_eq_elim #a s1 s2  =
  assert (length s1 == Seq.length s1.arr);
  assert (length s2 == Seq.length s2.arr);
  assert (forall (i: nat). i < length s1 ==> select s1 i == Seq.index s1.arr i);
  assert (forall (i: nat). i < length s1 ==> select s2 i == Seq.index s2.arr i);
  Seq.lemma_eq_elim (s1.arr) (s2.arr)

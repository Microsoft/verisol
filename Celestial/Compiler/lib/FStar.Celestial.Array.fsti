(* This library provides an abstract type array and models Solidity array semantics. *)

module FStar.Celestial.Array
open FStar.Celestial

val array (a:Type u#a) :Type u#a 

val create_empty: #a:Type -> a -> Tot (array a)
val length      : #a:Type -> s:array a -> Tot (n:nat{n <= uint_max})
val update      : #a:Type -> s:array a -> i:nat{i < length s} -> a -> Tot (array a)
val push        : #a:Type -> s:array a{length s < uint_max} -> a -> Tot (array a)
val select      : #a:Type -> s:array a -> i:nat{i < length s} -> Tot a 
val delete      : #a:Type -> s:array a -> i:nat{i < length s} -> Tot (array a)
val def_of      : #a:Type -> s:array a -> GTot a
val pop         : #a:Type -> s:array a{length s > 0} -> Tot (array a)

(* Lemmas on length *)

val lemma_len_create: #a:Type -> i:a ->
                      Lemma (ensures (length (create_empty i) = 0))
                      [SMTPat (length (create_empty i))]

val lemma_len_update1: #a:Type -> s:array a -> i:nat{i < length s} -> v:a ->
                       Lemma (ensures (length (update s i v) = length s))
                       [SMTPat (length (update s i v))]
 
val lemma_len_push: #a:Type -> s:array a{length s < uint_max} -> v:a ->
                    Lemma (ensures length (push s v) = (length s) + 1)
                    [SMTPat (length (push s v))]

val lemma_len_pop: #a:Type -> s:array a{length s > 0} ->
                   Lemma (ensures (length (pop s) == (length s) - 1))
                   [SMTPat (length (pop s))]

val lemma_len_del: #a:Type -> s:array a -> i:nat{i < length s} ->
                   Lemma (ensures (length (delete s i) = length s))
                   [SMTPat (length (delete s i))]

(* Lemmas on select *)

val lemma_select_update1: #a:Type -> s:array a -> i:nat{i < length s} -> v:a ->
                          Lemma (ensures (select (update s i v) i == v))
                          [SMTPat (select (update s i v) i)]

val lemma_select_update2: #a:Type -> s:array a -> j:nat{j < length s} -> v:a -> i:nat{i =!= j /\ i < length s} ->
                          Lemma (ensures (select (update s j v) i == select s i))
                          [SMTPat (select (update s j v) i)]

val lemma_select_push: #a:Type -> s:array a{length s < uint_max} -> v:a ->
                       Lemma (ensures (select (push s v) (length s)) == v)
                       [SMTPat (select (push s v) (length s))]

val lemma_select_push2: #a:Type -> s:array a{length s < uint_max} -> v:a -> i:nat{i < length s} ->
                       Lemma (ensures (select (push s v) i) == (select s i))
                       [SMTPat (select (push s v) i)]

val lemma_select_pop: #a:Type -> s:array a{length s > 1} -> i:nat{i < (length s) - 1} ->
                      Lemma (ensures (select (pop s) i) == select s i)
                      [SMTPat (select (pop s) i)]

val lemma_select_delete1: #a:Type -> s:array a -> i:nat{i < length s} ->
                          Lemma (ensures select #a (delete #a s i) i == def_of s)
                          [SMTPat (select #a (delete #a s i) i)]

val lemma_select_delete2: #a:Type -> s:array a -> i:nat{i < length s} -> j:nat{j < length s} ->
                          Lemma (ensures (i =!= j ==> select #a (delete #a s i) j == select #a s j))
                          [SMTPat (select #a (delete #a s i) j)]

(* Lemmas on default value *)

val lemma_default_update: #a:Type -> s:array a -> i:nat{i < length s} -> v:a ->
                          Lemma (ensures def_of (update s i v) == def_of s)
                          [SMTPat (def_of (update s i v))]

val lemma_default_push: #a:Type -> s:array a{length s < uint_max} -> v:a ->
                        Lemma (ensures def_of (push s v) == def_of s)
                        [SMTPat (def_of (push s v))]

val lemma_default_pop: #a:Type -> s:array a{length s > 0} ->
                       Lemma (ensures def_of (pop s) == def_of s)
                       [SMTPat (def_of (pop s))]

val lemma_default_delete: #a:Type -> s:array a -> i:nat{i < length s} ->
                          Lemma (ensures def_of (delete s i) == def_of s)
                          [SMTPat (def_of (delete s i))]

val lemma_pop_empty: #a:Type -> s:array a{length s == 1} ->
                     Lemma (ensures (pop #a s) == create_empty #a (def_of s))
                     [SMTPat (pop s)]

val equal (#a:Type) (s1:array a) (s2:array a): Type0

val lemma_eq_intro: #a:Type -> s1:array a -> s2:array a ->
                    Lemma (requires (length s1 = length s2 /\ (forall (i:nat{i < length s1}).{:pattern (select s1 i); (select s2 i)} (select s1 i == select s2 i)) /\ (def_of s1) == (def_of s2)))
                          (ensures (equal s1 s2))
                    [SMTPat (equal s1 s2)]

val lemma_eq_refl: #a:Type -> s1:array a -> s2:array a ->
                   Lemma (requires (s1 == s2))
                         (ensures (equal s1 s2))
                   [SMTPat (equal s1 s2)]

val lemma_eq_elim: #a:Type -> s1:array a -> s2:array a -> 
                   Lemma (requires (equal s1 s2)) 
                         (ensures (s1 == s2))
                   [SMTPat (equal s1 s2)]

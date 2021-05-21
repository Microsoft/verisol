module FStar.Celestial.Map

open FStar.OrdSet
module S = FStar.OrdSet
open FStar.FunctionalExtensionality
module F = FStar.FunctionalExtensionality

type total_order (a:eqtype) (f: (a -> a -> Tot bool)) =
    (forall a1 a2. (f a1 a2 /\ f a2 a1)  ==> a1 = a2) (* anti-symmetry *)
 /\ (forall a1 a2 a3. f a1 a2 /\ f a2 a3 ==> f a1 a3) (* transitivity  *)
 /\ (forall a1 a2. f a1 a2 \/ f a2 a1)                (*   totality    *)

let cmp (a:eqtype) = f:(a -> a -> Tot bool){total_order a f}

val t (key:eqtype) (value:Type) (f:cmp key) : Type u#a 

val const       : #key:eqtype -> #value:Type -> #f:cmp key -> x:value -> Tot (t key value f)
val sel         : #key:eqtype -> #value:Type -> #f:cmp key -> t key value f -> key -> Tot value
val domain      : #key:eqtype -> #value:Type -> #f:cmp key -> t key value f -> Tot (ordset key f)
val upd         : #key:eqtype -> #value:Type -> #f:cmp key -> t key value f -> key -> value -> Tot (t key value f)
val contains    : #key:eqtype -> #value:Type -> #f:cmp key -> t key value f -> key -> Tot bool 
val delete      : #key:eqtype -> #value:Type -> #f:cmp key -> t key value f -> key -> Tot (t key value f)
val choose      : #key:eqtype -> #value:Type -> #f:cmp key -> t key value f -> Tot (option (key * value))
val size        : #key:eqtype -> #value:Type -> #f:cmp key -> t key value f -> Tot nat
val equal       : #key:eqtype -> #value:Type -> #f:cmp key -> t key value f -> t key value f -> Tot prop
val def_of      : #key:eqtype -> #value:Type -> #f:cmp key -> t key value f -> Tot value
val mappings_of : #key:eqtype -> #value:Type -> #f:cmp key -> t key value f -> Tot (key ^-> value)

val size_const: #k:eqtype -> #v:Type -> #f:cmp k -> x:v ->
                Lemma (requires True)
                      (ensures (size #k #v #f (const #k #v #f x) = 0))
                [SMTPat (size #k #v #f (const #k #v #f x))]
                   
val size_delete: #k:eqtype -> #v:Type -> #f:cmp k -> y:k -> m:t k v f ->
                 Lemma (requires (contains #k #v #f m y))
                       (ensures (size #k #v #f m = size #k #v #f (delete #k #v #f m y) + 1))
                 [SMTPat (size #k #v #f (delete #k #v #f m y))]

val eq_intro: #k:eqtype -> #v:Type -> #f:cmp k -> m1:t k v f -> m2:t k v f ->
              Lemma (requires (S.equal (domain m1) (domain m2))/\(def_of m1 == def_of m2) /\ F.feq (mappings_of m1) (mappings_of m2) )  
                    (ensures (equal m1 m2))
              [SMTPat (equal m1 m2)]
  
val eq_lemma: #k:eqtype -> #v:Type -> #f:cmp k -> m1:t k v f -> m2:t k v f ->
              Lemma (requires (equal m1 m2))
                    (ensures (m1 == m2))
              [SMTPat (equal m1 m2)]

val sel_lemma: #k:eqtype -> #v:Type -> #f:cmp k -> x:k -> m:t k v f ->
              Lemma (requires True)
                    (ensures 
                        (if contains m x then (sel m x == mappings_of m x)
                        else (sel m x == def_of m)))
              [SMTPat (sel m x)]

val sel_upd1: #k:eqtype -> #v:Type -> #f:cmp k -> x:k -> y:v -> m:t k v f ->
              Lemma (requires True)
                    (ensures sel (upd m x y) x == y)
              [SMTPat (sel (upd m x y) x)]

val sel_upd2: #k:eqtype -> #v:Type -> #f:cmp k -> x:k -> y:v -> x':k -> m:t k v f ->
              Lemma (requires True)
                    (ensures (x =!= x' ==> (sel (upd m x y) x' == sel m x')))
              [SMTPat (sel (upd m x y) x')]

val sel_const: #k:eqtype -> #v:Type -> #f:cmp k -> x:k -> y:v ->
               Lemma (requires True)
                     (ensures (sel (const #k #v #f y) x == y))
               [SMTPat (sel (const #k #v #f y) x)]

val contains_upd1: #k:eqtype -> #v:Type -> #f:cmp k -> x:k -> y:v -> x':k -> m:t k v f ->
                   Lemma (requires True)
                         (ensures (contains (upd m x y) x' = (x = x' || contains m x')))
                   [SMTPat (contains (upd m x y) x')]

val contains_upd2: #k:eqtype -> #v:Type -> #f:cmp k -> x:k -> y:v -> x':k -> m:t k v f ->
                   Lemma (requires True)
                         (ensures (x =!= x' ==> (contains (upd m x y) x' = contains m x')))
                   [SMTPat (contains (upd m x y) x')]

val domain_upd : #key:eqtype -> #value:Type -> #f:cmp key -> m: t key value f -> k:key -> v:value ->
                 Lemma (requires True)
                       (ensures (domain (upd m k v)) == (S.union (domain m) (singleton k)))
                 [SMTPat (domain (upd m k v))]

val domain_upd2 : #key:eqtype -> #value:Type -> #f:cmp key -> m: t key value f -> k:key -> v:value ->
                 Lemma (requires True)
                       (ensures (contains m k) ==> OrdSet.equal (domain (upd m k v)) (domain m) )
                 [SMTPat (domain (upd m k v))]

val domain_check : #key:eqtype -> #value:Type -> #f:cmp key -> m: t key value f -> k:key -> v:value ->
                 Lemma (requires True)
                       (ensures (not (contains m k)) ==> OrdSet.equal ((empty)) (OrdSet.intersect (domain m) (singleton k)) )
                 [SMTPat (domain (upd m k v))]

val def_of_upd: #k:eqtype -> #v:Type -> #f:cmp k -> x:k -> y:v -> m:t k v f ->
                Lemma (requires True)
                      (ensures (def_of m) == (def_of (upd m x y)))
                [SMTPat (def_of (upd m x y))]

val def_of_delete: #k:eqtype -> #v:Type -> #f:cmp k -> x:k -> m:t k v f ->
                   Lemma (requires True)
                         (ensures (def_of (delete #k #v #f m x) == def_of m))
                   [SMTPat (def_of (delete #k #v #f m x))]
                   
val def_of_const: #k:eqtype -> #v:Type -> #f:cmp k -> y:v ->
                  Lemma (requires True)
                        (ensures def_of (const #k #v #f y) == y)
                  [SMTPat (def_of (const #k #v #f y))]

val sel_del2: #k:eqtype -> #v:Type -> #f:cmp k -> x:k -> x':k -> m:t k v f ->
              Lemma (requires True)
                    (ensures (x =!= x' ==> sel (delete m x) x' == sel m x'))
              [SMTPat (sel (delete m x) x')]

val upd_order: #k:eqtype -> #v:Type -> #f:cmp k -> x:k -> y:v -> x':k -> y':v -> m:t k v f ->
               Lemma (requires (x =!= x'))
                     (ensures (equal (upd (upd m x' y') x y) (upd (upd m x y) x' y')))
               [SMTPat (upd (upd m x' y') x y)] //This pattern is too aggresive; it will fire for any pair of upds
                  
val upd_same_k: #k:eqtype -> #v:Type -> #f:cmp k -> x:k -> y:v -> y':v -> m:t k v f ->
                Lemma (requires True)
                      (ensures (equal (upd (upd m x y') x y) (upd m x y)))
                [SMTPat (upd (upd m x y') x y)] //This pattern is too aggresive; it will fire for any pair of upds

val domain_const: #k:eqtype -> #v:Type -> #f:cmp k -> y:v ->
                  Lemma (requires True)
                        (ensures (domain #k #v #f (const #k #v #f y) = S.empty))
                  [SMTPat (domain #k #v #f (const #k #v #f y))]

val contains_delete: #k:eqtype -> #v:Type -> #f:cmp k -> x:k -> y:k -> m:t k v f ->
                     Lemma (requires True)
                           (ensures (contains (delete #k #v #f m y) x = (contains #k #v #f m x && not (x = y))))
                     [SMTPat (contains #k #v #f (delete #k #v #f m y) x)]

val contains_const: #k:eqtype -> #v:Type -> #f:cmp k -> y:v -> x:k ->
                     Lemma (requires True)
                           (ensures ~ (contains #k #v #f (const #k #v #f y) x))
                     [SMTPat (contains #k #v #f (const #k #v #f y) x)]

val eq_delete: #k:eqtype -> #v:Type -> #f:cmp k -> x:k -> m:t k v f ->
              Lemma (requires (not (mem x (domain #k #v #f m))))
                    (ensures (equal m (delete #k #v #f m x)))
              [SMTPat (delete #k #v #f m x)] 

val delete_upd: #k:eqtype -> #v:Type -> #f:cmp k -> x:k -> x':k -> y':v -> m:t k v f ->
                Lemma (requires (x =!= x'))
                      (ensures (equal (delete #k #v #f (upd m x' y') x) (upd (delete #k #v #f m x) x' y')))
                [SMTPat (delete #k #v #f (upd #k #v #f m x' y') x)] 

val delete_upd_same_k: #k:eqtype -> #v:Type -> #f:cmp k -> x:k -> y:v -> m:t k v f ->
                       Lemma (requires True)
                             (ensures (equal (delete #k #v #f (upd m x y) x) (delete #k #v #f m x)))
                       [SMTPat (delete #k #v #f (upd m x y) x)] // Is this pattern is too aggresive; will it fire for any pair of upds?

val upd_delete_same_k: #k:eqtype -> #v:Type -> #f:cmp k -> x:k -> y:v -> m:t k v f ->
                       Lemma (requires True)
                             (ensures (equal (upd (delete #k #v #f m x) x y) (upd m x y)))
                       [SMTPat (upd (delete #k #v #f m x) x y)] //Is this pattern is too aggresive; will it fire for any pair of upds?

val choose_const: #k:eqtype -> #v:Type -> #f:cmp k -> y:v ->
                  Lemma (requires True) 
                        (ensures (None? (choose #k #v #f (const #k #v #f y))) <==> (domain #k #v #f (const #k #v #f y) == S.empty))
                  [SMTPat (choose #k #v #f (const #k #v #f y))]

val choose_m: #k:eqtype -> #v:Type -> #f:cmp k -> m:t k v f ->
              Lemma (requires (~ ((domain m) == S.empty)))
                    (ensures (Some? (choose #k #v #f m)
                             /\ (sel #k #v #f m (fst (Some?.v (choose #k #v #f m))) == (snd (Some?.v (choose #k #v #f m))))
                             /\ (equal m (upd #k #v #f (delete #k #v #f m (fst (Some?.v (choose #k #v #f m))))
                                                       (fst (Some?.v (choose #k #v #f m)))
                                                       (snd (Some?.v (choose #k #v #f m)))
                                         )
                                )
                             )
                    )
              [SMTPat (choose #k #v #f m)]

val choose_upd: #k:eqtype -> #v:Type -> #f:cmp k -> m:t k v f -> x:k -> y:v ->
                Lemma (requires True)
                      (ensures (Some? (choose (upd m x y))))

val choose_not_already_exist: #k:eqtype -> #v:Type -> #f:cmp k -> m:t k v f -> x:k -> y:v ->
                              Lemma (requires True)
                              (ensures (
                                    choose_upd m x y;
                                    (x  <> (fst (Some?.v (choose (upd m x y))))) ==> (size m > 0)))

val non_zero_size_choose: #k:eqtype -> #v:Type -> #f:cmp k -> m:t k v f ->
                          Lemma
                          (requires size m > 0)
                          (ensures Some? (choose m))

val contains_choose: #k:eqtype -> #v:Type -> #f:cmp k -> m:t k v f ->
                     Lemma
                     (requires (Some? (choose m)))
                     (ensures contains m (fst (Some?.v (choose m))))

val size_upd1: #k:eqtype -> #v:Type -> #f:cmp k -> m:t k v f -> x:k -> y:v 
                -> Lemma (requires True)
                         (ensures  (not (contains m x) ==> size (upd m x y) == size m + 1))

val size_upd2: #k:eqtype -> #v:Type -> #f:cmp k -> m:t k v f -> x:k -> y:v 
                -> Lemma (requires True)
                         (ensures (contains m x) ==> size (upd m x y) == size m)

val size_upd: #k:eqtype -> #v:Type -> #f:cmp k -> m:t k v f -> x:k -> y:v ->  Lemma
(requires (True))
(ensures  (
               (contains #k #v #f m x ==> size (upd m x y) == size m) /\
                ((~ (contains #k #v #f m x)) ==> size (upd m x y) == size m + 1) /\
                (size (upd m x y) > 0))   )


val choose_after_update: #k:eqtype -> #v:Type -> #f:cmp k -> m:t k v f -> x:k -> y:v -> Lemma 
(requires (size m > 0))
(ensures (       
      (x  <> (fst (Some?.v (choose (upd m x y))))) ==>
            ((fst (Some?.v (choose m))) == ((fst (Some?.v (choose (upd m x y)))))) /\
            ((snd (Some?.v (choose m))) == ((snd (Some?.v (choose (upd m x y))))))
))

val choose_commute_up_del: #k:eqtype -> #v:Type -> #f:cmp k -> m:t k v f -> x:k -> y:v -> Lemma 
(requires (size m >0))
(ensures (
      (  x <> (fst (Some?.v (choose (upd m x y)))) 
            \/  ((contains m x) /\ (x <> (fst (Some?.v (choose m)))))
      ) ==>      
      (
            let k'' = fst (Some?.v (choose m)) in
            let rest_m = (delete m k'') in
            let m1 = (upd m x y) in
            let k' = fst (Some?.v (choose m1)) in 
            let rest_m1 = delete m1 k' in
            (upd rest_m x y) == rest_m1
      )
)
)

val empty_contains: #k:eqtype -> #v:Type -> #f:cmp k -> m:t k v f -> x:k ->
                    Lemma
                    (requires size m == 0)
                    (ensures ~ (contains m x))

val contains_size: #k:eqtype -> #v:Type -> #f:cmp k -> m:t k v f -> x:k ->
                   Lemma
                   (requires contains m x)
                   (ensures size m > 0)

val delete_upd_cancel_out: #k:eqtype -> #v:Type -> #f:cmp k -> m:t k v f -> x:k{~ (contains m x)} -> y:v ->
                           Lemma
                           (requires True)
                           (ensures (delete (upd m x y) x) == m)

let rec fold (#key:eqtype) (#value:_) (#accType:_) (#f:_) (g:key -> value -> accType -> accType) (m:t key value f) (a:accType)
: Tot accType (decreases (size m))
= if size m = 0 then a
  else begin
    non_zero_size_choose m;
    let Some (k, v) = choose m in
    let rest_m = delete m k in
    contains_choose m;
    fold g (delete m k) (g k v a)
  end


module FStar.Celestial.Map

open FStar.OrdSet
module S = FStar.OrdSet
open FStar.FunctionalExtensionality
module F = FStar.FunctionalExtensionality

noeq
type t (key:eqtype) (value:Type) (f:cmp key)
= {
  domain:   S.ordset key f;
  def:  value;
  mappings:m:(key ^-> value){forall x. (~ (S.mem x domain)) ==> m x == def}
}

let const #key #value #f v
= {
  domain   = S.empty;
  def      = v;
  mappings = F.on key (fun _ -> v);
}

let sel #key #value #f m k
= m.mappings k
    
let domain #key #value #f m
= m.domain

let upd #key #value #f m k v
= {
    mappings = F.on key (fun x -> if x = k then v else m.mappings x);
    domain   = (S.union (domain m) (singleton k));
    def      = m.def
}
        
let contains #key #value #f m k
= S.mem k m.domain

let delete #key #value #f m k
= { 
    domain = S.remove k (m.domain);
    def = m.def;
    mappings =  F.on key (fun x -> if x = k then m.def else m.mappings x);
}

let choose #key #value #f m
= match OrdSet.choose (m.domain) with
    | None   -> None
    | Some x -> Some (x, (m.mappings x))

let size #key #value #f m
= S.size m.domain

let equal #key #value #f m1 m2
= F.feq m1.mappings m2.mappings /\
  S.equal m1.domain m2.domain /\
  m1.def == m2.def


let def_of #key #value #f m
= m.def

let mappings_of #key #value #f m
= m.mappings

let size_const #key #value #f x = ()
let size_delete #key #value #f y m = ()
let eq_intro #key #value #f m1 m2 = ()
let eq_lemma #key #value #f m1 m2 = ()
let sel_lemma #key #value #f x m = ()
let sel_upd1 #key #value #f x y m = ()
let sel_upd2 #key #value #f x y x' m = ()
let sel_const #key #value #f x y = ()
let contains_upd1 #key #value #f x y x' m = ()
let contains_upd2 #key #value #f x y x' m = ()
let domain_upd #key #value #f m k v = ()
let domain_upd2 #key #value #f m k v = ()
let domain_check #key #value #f m k v = ()
let def_of_upd #key #value #f x y m = ()
let def_of_delete #key #value #f x m = ()
let def_of_const #key #value #f y = ()
let sel_del2 #key #value #f x x' m = ()
let upd_order #key #value #f x y x' y' m = ()
let upd_same_k #key #value #f x y y' m = ()
let domain_const #key #value #f y = ()
let contains_delete #key #value #f x y m = ()
let contains_const #key #value #f y k = ()
let eq_delete #key #value #f x m = ()
let delete_upd #key #value #f x x' y' m = ()
let delete_upd_same_k #key #value #f x y m = ()
let upd_delete_same_k #key #value #f x y m = ()
let choose_const #key #value #f y = ()
let choose_m #key #value #f m = ()
let choose_upd #key #value #f m x y = ()
let choose_not_already_exist #key #value #f m x y = ()
let non_zero_size_choose #_ #_ #_ _ = ()

let contains_choose #_ #_ #_ _ = ()
let size_upd1 #k #v #f m x y = 
    let m1 = (upd m x y) in 
    if (not (mem x m.domain)) then OrdSet.size_union2 #k #f (m.domain) x; () 

let size_upd2 #k #v #f m x y = 
    let m1 = (upd m x y) in 
    if (mem x m.domain) then OrdSet.size_union3 #k #f (m.domain) x; () 

let size_upd #_ #_ #_ m x y = size_upd1 m x y; size_upd2 m x y; ()

let choose_after_update #key #value #f m x y =       
      choose_upd m x y; 
      non_zero_size_choose m;

      let m1 = (upd m x y) in
      let k' = (fst (Some?.v (choose m1))) in 
      let k'' = (fst (Some?.v (choose m))) in 
      if (x  <> k') then OrdSet.choose_upd_det (m.domain) x ; ()
      

let choose_commute_up_del #key #value #f m x y = 
    choose_upd m x y;  
    non_zero_size_choose m; 
    let k'' = fst (Some?.v (choose m)) in
    let rest_m = (delete m k'') in
    let m1 = (upd m x y) in
    let k' = fst (Some?.v (choose m1)) in 
    let rest_m1 = delete m1 k' in
      if (x <> k') then OrdSet.choose_upd_det (m.domain) x      
      else  
      if ((contains m x) && (x <> k'') ) then OrdSet.choose_upd_det2 (m.domain) x

let empty_contains #_ #_ #_ _ _ = ()
let contains_size #_ #_ #_ _ _ = ()
let delete_upd_cancel_out #k #v #f m x y = ()

let fold_base_case _ _ _ = ()
let fold_induction _ _ _ = ()

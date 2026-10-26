import Dmp.R1Mass

/-!
# Information tiers and privacy singularities

The owner, 2026-10-26 13:31Z, redrafting C12 and C13 as routing: "Tier 1 information is the information in the MW net, which is always
consulted first and then outward as the VIBE net dictates. ... Tier 2 information is the one, where the MW net has to route you to a
VIBE net source, Tier 3 to outside the Event Horizon through an exit node." And: "Privacy could be seen as singularities of VIBE space
where not even the MW-net can pull the information out."

The model: a query goes to the Milky Way net first, then to the VIBE sources in the order the field gives, then to the exit node. An
item inside a privacy singularity is never handed out by any tier. What this file proves:

1. The Milky Way net answers first: when it holds an answer, no VIBE source and no exit node is asked.
2. The exit node is reached only when no tier inside the Event Horizon has an answer.
3. No route ever returns an item inside a singularity.
4. A singularity is invisible: routing is exactly the same as if the singular items did not exist anywhere, so an answer reveals
   nothing about what a singularity holds (not even the Milky Way net can pull it out).
-/

namespace Dmp

/-- The tier an answer came from. -/
inductive TrTier where
  | milkyWay
  | vibe
  | exit
deriving DecidableEq

/-- A source as the router sees it: for each query, what it holds, if anything. -/
abbrev TrSource (Q I : Type) := Q → Option I

/-- What a source may hand out: nothing from inside a singularity. -/
def trVisible {I : Type} (sing : I → Bool) : Option I → Option I
  | some i => if sing i then none else some i
  | none => none

/-- The first VIBE source, in field order, with a visible answer. -/
def trFirst {Q I : Type} (sing : I → Bool) (q : Q) : List (TrSource Q I) → Option I
  | [] => none
  | f :: fs =>
    match trVisible sing (f q) with
    | some i => some i
    | none => trFirst sing q fs

/-- The route: the Milky Way net first, then the VIBE sources in field order, then the exit node. -/
def trRoute {Q I : Type} (sing : I → Bool) (mw : TrSource Q I) (vibe : List (TrSource Q I)) (exit : TrSource Q I) (q : Q) :
    Option (TrTier × I) :=
  match trVisible sing (mw q) with
  | some i => some (.milkyWay, i)
  | none =>
    match trFirst sing q vibe with
    | some i => some (.vibe, i)
    | none =>
      match trVisible sing (exit q) with
      | some i => some (.exit, i)
      | none => none

theorem tr_visible_not_singular {I : Type} (sing : I → Bool) (o : Option I) (i : I) (h : trVisible sing o = some i) :
    sing i = false := by
  cases o with
  | none => simp [trVisible] at h
  | some j =>
    by_cases hs : sing j = true
    · simp [trVisible, hs] at h
    · simp [trVisible, hs] at h
      rw [← h]
      exact Bool.of_not_eq_true hs

theorem tr_first_not_singular {Q I : Type} (sing : I → Bool) (q : Q) (fs : List (TrSource Q I)) (i : I)
    (h : trFirst sing q fs = some i) : sing i = false := by
  induction fs with
  | nil => simp [trFirst] at h
  | cons f fs ih =>
    cases hv : trVisible sing (f q) with
    | none => simp [trFirst, hv] at h; exact ih h
    | some j =>
      simp [trFirst, hv] at h
      rw [← h]
      exact tr_visible_not_singular sing (f q) j hv

/-- The Milky Way net answers first. -/
theorem tr_milky_way_first {Q I : Type} (sing : I → Bool) (mw : TrSource Q I) (vibe : List (TrSource Q I)) (exit : TrSource Q I)
    (q : Q) (i : I) (h : trVisible sing (mw q) = some i) : trRoute sing mw vibe exit q = some (.milkyWay, i) := by
  unfold trRoute
  rw [h]

/-- The exit node only when nothing inside the Event Horizon answers. -/
theorem tr_exit_last {Q I : Type} (sing : I → Bool) (mw : TrSource Q I) (vibe : List (TrSource Q I)) (exit : TrSource Q I)
    (q : Q) (i : I) (h : trRoute sing mw vibe exit q = some (.exit, i)) :
    trVisible sing (mw q) = none ∧ trFirst sing q vibe = none := by
  cases hm : trVisible sing (mw q) with
  | some j => simp [trRoute, hm] at h
  | none =>
    cases hv : trFirst sing q vibe with
    | some j => simp [trRoute, hm, hv] at h
    | none => exact ⟨rfl, rfl⟩

/-- No route ever returns an item inside a singularity. -/
theorem tr_never_singular {Q I : Type} (sing : I → Bool) (mw : TrSource Q I) (vibe : List (TrSource Q I)) (exit : TrSource Q I)
    (q : Q) (t : TrTier) (i : I) (h : trRoute sing mw vibe exit q = some (t, i)) : sing i = false := by
  cases hm : trVisible sing (mw q) with
  | some j =>
    simp [trRoute, hm] at h
    rw [← h.2]
    exact tr_visible_not_singular sing (mw q) j hm
  | none =>
    cases hv : trFirst sing q vibe with
    | some j =>
      simp [trRoute, hm, hv] at h
      rw [← h.2]
      exact tr_first_not_singular sing q vibe j hv
    | none =>
      cases he : trVisible sing (exit q) with
      | none => simp [trRoute, hm, hv, he] at h
      | some j =>
        simp [trRoute, hm, hv, he] at h
        rw [← h.2]
        exact tr_visible_not_singular sing (exit q) j he

/-! ## A singularity is invisible -/

/-- A source with every singular item removed: what the world would look like if the singularity held nothing. -/
def trScrub {Q I : Type} (sing : I → Bool) (f : TrSource Q I) : TrSource Q I := fun q => trVisible sing (f q)

theorem tr_visible_idem {I : Type} (sing : I → Bool) (o : Option I) : trVisible sing (trVisible sing o) = trVisible sing o := by
  cases o with
  | none => rfl
  | some j =>
    by_cases hs : sing j = true
    · simp [trVisible, hs]
    · simp [trVisible, hs]

theorem tr_first_scrub {Q I : Type} (sing : I → Bool) (q : Q) (fs : List (TrSource Q I)) :
    trFirst sing q (fs.map (trScrub sing)) = trFirst sing q fs := by
  induction fs with
  | nil => rfl
  | cons f fs ih =>
    simp only [List.map_cons, trFirst, trScrub, tr_visible_idem]
    cases trVisible sing (f q) with
    | some i => rfl
    | none => exact ih

/-- Routing over the real sources equals routing over sources from which every singular item was removed: the answer, and whether
there is one, says nothing about what any singularity holds, in the Milky Way net, in a VIBE source or beyond the exit node. -/
theorem tr_singularity_invisible {Q I : Type} (sing : I → Bool) (mw : TrSource Q I) (vibe : List (TrSource Q I))
    (exit : TrSource Q I) (q : Q) :
    trRoute sing (trScrub sing mw) (vibe.map (trScrub sing)) (trScrub sing exit) q = trRoute sing mw vibe exit q := by
  unfold trRoute
  simp only [trScrub, tr_visible_idem, tr_first_scrub]

end Dmp

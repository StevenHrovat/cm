import Dmp.Layouts

/-!
# The Cosmic Microvibe Background: one file that proves its own recipe

The owner, 2026-10-26 18:19Z: "A CMOS galaxy is reducible to a single .cmb file, that also proves it's recipe through DoT hashing of
the produced VIBE field. Let's call it the Cosmic Microvibe Background." And 18:23Z: the Big Bang that deploys from it is signed, in a
cold start, by the Duality that produced it, so that the same environment can verify its own cold start by reproducing the same CMB; a
cold start redeploys instantly when the live field matches, and goes through the normal inflation and Big Bang when it does not; the plan
for the second path is imprinted on the White Key next to the file.

This file says what a `.cmb` must carry so that it proves itself, and proves what can be proven about it:

1. **The file determines everything Gravity sees.** A recipe is written as a list of line codes: kind, parent, cluster and each half's
   membership per declared failure class. Read back, the recipe agrees with the original on every declared class, and two recipes that
   agree there have the same well-formedness, chains, decisions, digest and verdict. Nothing Gravity reads is outside the file.
2. **The digest is not the recipe.** Two well-formed recipes can carry the same VIBE digest and decide differently (a counterexample the
   kernel checks). The field's digest proves that a live system spans the field of the file's recipe (T33), never which recipe was meant:
   the recipe itself is in the file and the digest beside it.
3. **Two independent halves seal it.** A half signs only when its own build recomputes the file's verdict and digest from the file's recipe
   (`R1Mass.lean`'s pattern: each witness recomputes with its own build). The sealing Duality is the galaxy's own, line 0 of every
   well-formed recipe, which the theory proves minted: a sealed file has two independent signers by theorem, not by configuration. A
   sealed file marked proven is a DMP-complete recipe under either half's ledger.
4. **The cold start.** The redeploy path is taken exactly when the measured field is the file's digest; on a sealed file that means the
   live system spans the field of the proven recipe. The Big Bang path starts from the same proven recipe: the match selects the path,
   never the proof. A file is evidence only in the environment whose DMP and Gravity pins it names (T-P: trust never crosses instances);
   elsewhere it is refused before either path.

Name: in DMP, CMB is Black Hole's observability layer, "counts, never content" (chapter 06). The `.cmb` file is what T33 already asks each
stable to publish (the canonical field, hashed, next to the proof-run hash), with the recipe, the pins and the seal added so that the
published thing proves itself. Hashing is outside the model: the digest here is the canonical field itself (depth, mass per line), and a
hash of it proves no more and no less than the digest does.
-/

namespace Dmp

/-! ## 1. The file's encoding of a recipe -/

/-- One line as the file carries it: the kind, the parent, the cluster, and each half's membership per declared failure class. -/
structure LineCode where
  kind : Kind
  parent : Option Nat
  cluster : Nat
  w1 : List (Nat × Nat)
  w2 : List (Nat × Nat)
  deriving DecidableEq, Repr

/-- A witness as the file writes it: its membership in each declared class, in the order of the list. -/
def Witness.encode (fcl : List FailureClass) (w : Witness) : List (Nat × Nat) := fcl.map fun c => (c, w.member c)

/-- A witness read back from the file. A class the file does not carry reads as member 0: the file is the whole witness. -/
def Witness.decode (ps : List (Nat × Nat)) : Witness := ⟨fun c => (ps.lookup c).getD 0⟩

/-- On every declared class the witness read back is the witness written. -/
theorem Witness.decode_encode (fcl : List FailureClass) (w : Witness) (c : FailureClass) (hc : c ∈ fcl) :
    (Witness.decode (w.encode fcl)).member c = w.member c := by
  induction fcl with
  | nil => exact absurd hc List.not_mem_nil
  | cons k ks ih =>
    by_cases h : c = k
    · subst h
      simp [Witness.encode, Witness.decode, List.lookup_cons]
    · have hne : (c == k) = false := by simpa using h
      have hc' : c ∈ ks := (List.mem_cons.mp hc).resolve_left h
      have := ih hc'
      simp only [Witness.encode, Witness.decode, List.map_cons, List.lookup_cons, hne] at this ⊢
      exact this

/-- A line as the file carries it: the person is always a declared class (Layer.lean, finding F1). -/
def Line.encode (fcl : List FailureClass) (l : Line) : LineCode :=
  ⟨l.kind, l.parent, l.cluster, l.dot.w1.encode (person :: fcl), l.dot.w2.encode (person :: fcl)⟩

def LineCode.decode (c : LineCode) : Line := ⟨c.kind, ⟨Witness.decode c.w1, Witness.decode c.w2⟩, c.parent, c.cluster⟩

/-- The recipe as the file carries it: one code per line, in line order. -/
def Recipe.encode (fcl : List FailureClass) (r : Recipe) : List LineCode := (List.range r.n).map fun i => (r.line i).encode fcl

/-- A line the file does not carry (nothing at or beyond `n` is a line of a recipe). -/
def blankLine : Line := ⟨.galaxy, ⟨⟨fun _ => 0⟩, ⟨fun _ => 0⟩⟩, none, 0⟩

/-- The recipe read back from the file. -/
def Recipe.decode (codes : List LineCode) : Recipe :=
  { n := codes.length, line := fun i => match codes[i]? with | some c => c.decode | none => blankLine }

/-- Two recipes agree on the declared classes: the same number of lines, each with the same kind, parent and cluster, and each half's
    witness the same on the person and every declared class. This is exactly what the file carries. -/
def Recipe.agreesOn (fcl : List FailureClass) (r r' : Recipe) : Prop :=
  r.n = r'.n ∧ ∀ i, i < r.n →
    (r.line i).kind = (r'.line i).kind ∧ (r.line i).parent = (r'.line i).parent ∧ (r.line i).cluster = (r'.line i).cluster ∧
    (∀ c ∈ person :: fcl, (r.line i).dot.w1.member c = (r'.line i).dot.w1.member c) ∧
    (∀ c ∈ person :: fcl, (r.line i).dot.w2.member c = (r'.line i).dot.w2.member c)

theorem Recipe.decode_encode_n (fcl : List FailureClass) (r : Recipe) : (Recipe.decode (r.encode fcl)).n = r.n := by
  simp [Recipe.decode, Recipe.encode]

theorem Recipe.decode_encode_line (fcl : List FailureClass) (r : Recipe) (i : Nat) (hi : i < r.n) :
    (Recipe.decode (r.encode fcl)).line i = ((r.line i).encode fcl).decode := by
  simp [Recipe.decode, Recipe.encode, List.getElem?_range hi]

/-- Reading the file back gives a recipe that agrees with the one written on every declared class. -/
theorem Recipe.decode_encode_agrees (fcl : List FailureClass) (r : Recipe) : r.agreesOn fcl (Recipe.decode (r.encode fcl)) := by
  refine ⟨(r.decode_encode_n fcl).symm, ?_⟩
  intro i hi
  rw [r.decode_encode_line fcl i hi]
  exact ⟨rfl, rfl, rfl, fun c hc => (Witness.decode_encode _ _ c hc).symm, fun c hc => (Witness.decode_encode _ _ c hc).symm⟩

/-! ### Two recipes that agree on the declared classes are the same to Gravity -/

theorem independentB_congr (fcl : List FailureClass) (a a' b b' : Witness) (ha : ∀ c ∈ fcl, a.member c = a'.member c)
    (hb : ∀ c ∈ fcl, b.member c = b'.member c) : independentB fcl a b = independentB fcl a' b' := by
  unfold independentB
  induction fcl with
  | nil => rfl
  | cons k ks ih =>
    simp only [List.all_cons]
    rw [ha k List.mem_cons_self, hb k List.mem_cons_self,
      ih (fun c hc => ha c (List.mem_cons_of_mem k hc)) (fun c hc => hb c (List.mem_cons_of_mem k hc))]

theorem Recipe.lineOkB_congr (fcl : List FailureClass) (r r' : Recipe) (h : r.agreesOn fcl r') (i : Nat) (hi : i < r.n) :
    r.lineOkB fcl i = r'.lineOkB fcl i := by
  obtain ⟨hk, hp, hc, h1, h2⟩ := h.2 i hi
  unfold Recipe.lineOkB DoT.mintedB
  rw [hk, hp, hc, independentB_congr _ _ _ _ _ h1 h2]

theorem Recipe.wfB_congr (fcl : List FailureClass) (r r' : Recipe) (h : r.agreesOn fcl r') : r.wfB fcl = r'.wfB fcl := by
  unfold Recipe.wfB
  rw [← h.1]
  apply Bool.eq_iff_iff.mpr
  simp only [List.all_eq_true, List.mem_range]
  constructor
  · intro hall i hi
    rw [← r.lineOkB_congr fcl r' h i hi]
    exact hall i hi
  · intro hall i hi
    rw [r.lineOkB_congr fcl r' h i hi]
    exact hall i hi

theorem Recipe.wf_congr (fcl : List FailureClass) (r r' : Recipe) (h : r.agreesOn fcl r') : r.wf fcl ↔ r'.wf fcl := by
  rw [← Recipe.wfB_iff, ← Recipe.wfB_iff, r.wfB_congr fcl r' h]

theorem Recipe.verdict_congr (fcl : List FailureClass) (r r' : Recipe) (h : r.agreesOn fcl r') (agree : Nat → Bool) :
    r.verdict fcl agree = r'.verdict fcl agree := by
  unfold Recipe.verdict
  rw [r.wfB_congr fcl r' h, h.1]

/-- The chain reads only parents, so two recipes with the same parents walk the same chains (once parents are earlier lines). -/
theorem Recipe.chainF_congr (fcl : List FailureClass) (r r' : Recipe) (hwf : r.wf fcl) (h : r.agreesOn fcl r') :
    ∀ fuel i, i < r.n → r.chainF fuel i = r'.chainF fuel i := by
  intro fuel
  induction fuel with
  | zero => intros; rfl
  | succ f ih =>
    intro i hi
    have hp := (h.2 i hi).2.1
    simp only [Recipe.chainF]
    cases hpar : (r.line i).parent with
    | none => rw [hp] at hpar; rw [hpar]
    | some p =>
      have hok := (hwf i hi).1
      rw [hpar] at hok
      rw [hp] at hpar
      rw [hpar]
      show i :: r.chainF f p = i :: r'.chainF f p
      rw [ih p (by omega)]

theorem Recipe.chain_congr (fcl : List FailureClass) (r r' : Recipe) (hwf : r.wf fcl) (h : r.agreesOn fcl r') (i : Nat) (hi : i < r.n) :
    r.chain i = r'.chain i :=
  r.chainF_congr fcl r' hwf h i i hi

theorem Recipe.decide_congr (fcl : List FailureClass) (r r' : Recipe) (hwf : r.wf fcl) (h : r.agreesOn fcl r') (v : Nat → Verdict)
    (i : Nat) (hi : i < r.n) : r.decide v i = r'.decide v i := by
  unfold Recipe.decide
  rw [r.chain_congr fcl r' hwf h i hi]

theorem Recipe.digest_congr' (fcl : List FailureClass) (r r' : Recipe) (hwf : r.wf fcl) (h : r.agreesOn fcl r') : r.digest = r'.digest := by
  unfold Recipe.digest Recipe.depth Recipe.mass
  rw [← h.1]
  apply List.map_congr_left
  intro i hi
  have hi' : i < r.n := List.mem_range.mp hi
  rw [r.chain_congr fcl r' hwf h i hi']
  congr 2
  congr 1
  apply List.filter_congr
  intro k hk
  rw [r.chain_congr fcl r' hwf h k (List.mem_range.mp hk)]

/-- The file determines everything Gravity sees: the verdict of the recipe read back is the verdict of the recipe written, under any
    ledger, and when the recipe is well formed so are its digest and every decision. -/
theorem Recipe.file_determines (fcl : List FailureClass) (r : Recipe) (agree : Nat → Bool) :
    (Recipe.decode (r.encode fcl)).verdict fcl agree = r.verdict fcl agree ∧
    (r.wf fcl → (Recipe.decode (r.encode fcl)).digest = r.digest ∧
      ∀ v i, i < r.n → (Recipe.decode (r.encode fcl)).decide v i = r.decide v i) := by
  have h := r.decode_encode_agrees fcl
  refine ⟨(r.verdict_congr fcl _ h agree).symm, fun hwf => ⟨(r.digest_congr' fcl _ hwf h).symm, fun v i hi => (r.decide_congr fcl _ hwf h v i hi).symm⟩⟩

/-! ## 2. The digest is not the recipe -/

namespace Layouts

/-- F. A galaxy, two knowledge layers over it, one layer under each. -/
def layoutF : Recipe :=
  { n := 5
    line := fun
      | 0 => line .galaxy galaxyDot none
      | 1 => line .information knowledgeDot (some 0)
      | 2 => line .information dwarfDot (some 0)
      | 3 => line .information knowledgeDot (some 1)
      | _ => line .information knowledgeDot (some 2) }

/-- F'. The same lines, with the two lowest layers swapped between the two knowledge layers. -/
def layoutF' : Recipe :=
  { layoutF with
    line := fun i =>
      if i = 3 then line .information knowledgeDot (some 2)
      else if i = 4 then line .information knowledgeDot (some 1)
      else layoutF.line i }

example : layoutF.verdict classes allAgree = .proven := rfl
example : layoutF'.verdict classes allAgree = .proven := rfl

/-- Both carry the same digest. -/
example : layoutF.digest = [(0, 5), (1, 2), (1, 2), (2, 1), (2, 1)] := rfl
example : layoutF'.digest = layoutF.digest := rfl

/-- The first knowledge layer refuses, everything else passes. -/
def refuseAt1 : Nat → Verdict := fun j => if j = 1 then .refuse else .pass

/-- Line 3 is under that layer in F and not in F': the two recipes decide differently. -/
example : layoutF.decide refuseAt1 3 = .refuse := rfl
example : layoutF'.decide refuseAt1 3 = .pass := rfl

end Layouts

/-- The VIBE digest is not injective: two well-formed, proven recipes share a digest and decide differently. So a digest proves that a
    system spans the field of a recipe, never which recipe (the field also forgets kinds, clusters and Dualities). -/
theorem digest_not_injective :
    ∃ r r' : Recipe, r.wf Layouts.classes ∧ r'.wf Layouts.classes ∧ r.digest = r'.digest ∧ ∃ v i, r.decide v i ≠ r'.decide v i :=
  ⟨Layouts.layoutF, Layouts.layoutF', (Recipe.wfB_iff _ _).mp rfl, (Recipe.wfB_iff _ _).mp rfl, rfl, Layouts.refuseAt1, 3, by decide⟩

/-! ## 3. Two independent halves seal it -/

/-- A pin: source, commit, version and hash of a vendored specification (the format of `vendor/dmp/PIN`). -/
structure Pin where
  source : String
  commit : String
  version : String
  sha256 : String
  deriving DecidableEq, Repr

/-- The Cosmic Microvibe Background: what one file carries so that it proves its own recipe. The DMP it was proven against and the
    Gravity that proved it, both pinned; the declared failure classes; the recipe, line by line; the expected VIBE field (T33); and
    Gravity's verdict. The seal (both halves' signatures) is over all of it and lives beside it. -/
structure Cmb where
  dmp : Pin
  gravity : Pin
  classes : List FailureClass
  recipe : List LineCode
  digest : List (Nat × Nat)
  verdict : Verdict3
  deriving DecidableEq

/-- A half's build: from the declared classes and the file's recipe, the verdict and the digest it computes itself. -/
abbrev Build := List FailureClass → List LineCode → Verdict3 × List (Nat × Nat)

/-- The reference build: Gravity's verdict and digest of the recipe read from the file, under the half's own lemma ledger. -/
def refBuild (agree : Nat → Bool) : Build :=
  fun fcl codes => ((Recipe.decode codes).verdict fcl agree, (Recipe.decode codes).digest)

/-- A half signs the file only when its own build gives the file's verdict and digest from the file's recipe. -/
def Cmb.signs (b : Build) (c : Cmb) : Bool := decide (b c.classes c.recipe = (c.verdict, c.digest))

/-- The file is sealed when both halves of the producing Duality signed it. -/
def Cmb.sealed (b1 b2 : Build) (c : Cmb) : Bool := c.signs b1 && c.signs b2

theorem Cmb.signs_iff (b : Build) (c : Cmb) : c.signs b = true ↔ b c.classes c.recipe = (c.verdict, c.digest) := by
  simp [Cmb.signs]

/-- A seal needs both signatures; one half never seals (Axiom 1). -/
theorem Cmb.sealed_needs_both (b1 b2 : Build) (c : Cmb) (h : c.sealed b1 b2 = true) : c.signs b1 = true ∧ c.signs b2 = true := by
  simpa [Cmb.sealed] using h

/-- A file sealed by two reference builds says what its own recipe says, under either half's ledger, and carries its field. -/
theorem Cmb.sealed_verdict (a1 a2 : Nat → Bool) (c : Cmb) (h : c.sealed (refBuild a1) (refBuild a2) = true) :
    (Recipe.decode c.recipe).verdict c.classes a1 = c.verdict ∧ (Recipe.decode c.recipe).verdict c.classes a2 = c.verdict ∧
    (Recipe.decode c.recipe).digest = c.digest := by
  obtain ⟨h1, h2⟩ := c.sealed_needs_both _ _ h
  have e1 := (c.signs_iff _).mp h1
  have e2 := (c.signs_iff _).mp h2
  simp only [refBuild, Prod.mk.injEq] at e1 e2
  exact ⟨e1.1, e2.1, e1.2⟩

/-- A sealed file marked proven is a DMP-complete recipe (Complete.lean), under both halves' ledgers. -/
theorem Cmb.sealed_proven_complete (a1 a2 : Nat → Bool) (c : Cmb) (h : c.sealed (refBuild a1) (refBuild a2) = true)
    (hp : c.verdict = .proven) :
    (Recipe.decode c.recipe).dmpComplete c.classes a1 ∧ (Recipe.decode c.recipe).dmpComplete c.classes a2 := by
  obtain ⟨h1, h2, _⟩ := c.sealed_verdict a1 a2 h
  exact ⟨(Recipe.verdict_proven_iff _ _ _).mp (h1.trans hp), (Recipe.verdict_proven_iff _ _ _).mp (h2.trans hp)⟩

/-- One build signs at most one verdict and one digest for one recipe: two files over the same recipe and classes that one half signed
    say the same (R1Mass.lean: on one signed state at most one layout is accepted). -/
theorem Cmb.one_recipe_one_seal (b : Build) (c c' : Cmb) (h : c.signs b = true) (h' : c'.signs b = true) (hc : c.classes = c'.classes)
    (hr : c.recipe = c'.recipe) : c.verdict = c'.verdict ∧ c.digest = c'.digest := by
  have e := (c.signs_iff b).mp h
  have e' := (c'.signs_iff b).mp h'
  rw [hc, hr] at e
  rw [e] at e'
  exact ⟨(Prod.mk.injEq _ _ _ _).mp e' |>.1, (Prod.mk.injEq _ _ _ _).mp e' |>.2⟩

/-- Line 0 of every well-formed recipe with a line is a galaxy with no parent and a minted Duality: no earlier line exists to be its
    parent, so the recipe requires it to be a root, and a root nobody gates must be a Duality (T38, G0). -/
theorem Recipe.line0_minted_galaxy (fcl : List FailureClass) (r : Recipe) (hwf : r.wf fcl) (hn : 0 < r.n) :
    (r.line 0).parent = none ∧ (r.line 0).kind = .galaxy ∧ (r.line 0).dot.minted fcl := by
  have hok := hwf 0 hn
  have hroot : (r.line 0).parent = none := by
    cases hp : (r.line 0).parent with
    | none => rfl
    | some j =>
      have h1 := hok.1
      rw [hp] at h1
      exact absurd h1.1 (Nat.not_lt_zero j)
  refine ⟨hroot, ?_, r.root_minted fcl hwf 0 hn hroot⟩
  have h1 := hok.1
  rw [hroot] at h1
  exact h1

/-- The Duality that produced the file: the galaxy's own, line 0 of the file's recipe. -/
def Cmb.producer (c : Cmb) : DoT := ((Recipe.decode c.recipe).line 0).dot

/-- In a sealed, proven file with a line, the producing Duality is minted: the two signers are independent by theorem, not by
    configuration. The owner, 18:23Z: the Big Bang "is also signed in the case of a cold start by the DoT that produced it". -/
theorem Cmb.producer_minted (a1 a2 : Nat → Bool) (c : Cmb) (h : c.sealed (refBuild a1) (refBuild a2) = true) (hp : c.verdict = .proven)
    (hn : c.recipe ≠ []) : c.producer.minted c.classes := by
  have hc := (c.sealed_proven_complete a1 a2 h hp).1
  have hn' : 0 < (Recipe.decode c.recipe).n := by
    simp only [Recipe.decode]
    exact List.length_pos_iff.mpr hn
  exact ((Recipe.decode c.recipe).line0_minted_galaxy c.classes hc.1 hn').2.2

/-- The empty recipe is well formed and proven under any ledger: there is no line to refuse and none to carry. Finding F9. -/
theorem empty_recipe_proven (fcl : List FailureClass) (agree : Nat → Bool) :
    (Recipe.mk 0 fun _ => blankLine).verdict fcl agree = .proven := rfl

/-! ## 4. The cold start -/

/-- The two paths of a cold start from a file. -/
inductive ColdStart where
  /-- The live field reproduces the file's digest: deploy from the file as it is. -/
  | redeploy
  /-- It does not: inflation and the Big Bang, from the file's recipe. -/
  | bigBang
  deriving DecidableEq, Repr

/-- The environment that restores: the DMP and the Gravity it runs, pinned. -/
structure Environment where
  dmp : Pin
  gravity : Pin

/-- The plan the file imprints for a cold start: refused outside the environment whose pins it names (trust never crosses instances,
    T-P); inside it, the path the measured field selects. -/
def Cmb.plan (c : Cmb) (env : Environment) (measured : List (Nat × Nat)) : Option ColdStart :=
  if c.dmp = env.dmp ∧ c.gravity = env.gravity then some (if c.digest = measured then .redeploy else .bigBang) else none

/-- Under another DMP or another Gravity the file is no evidence: no path at all. -/
theorem Cmb.plan_refused_elsewhere (c : Cmb) (env : Environment) (m : List (Nat × Nat))
    (h : c.dmp ≠ env.dmp ∨ c.gravity ≠ env.gravity) : c.plan env m = none := by
  unfold Cmb.plan
  rw [if_neg]
  intro ⟨h1, h2⟩
  cases h with
  | inl h => exact h h1
  | inr h => exact h h2

/-- Whether the file is evidence does not depend on the measured field: the field selects the path, never the standing of the file. -/
theorem Cmb.plan_isSome_iff (c : Cmb) (env : Environment) (m : List (Nat × Nat)) :
    (c.plan env m).isSome = true ↔ c.dmp = env.dmp ∧ c.gravity = env.gravity := by
  unfold Cmb.plan
  by_cases h : c.dmp = env.dmp ∧ c.gravity = env.gravity <;> simp [h]

/-- The redeploy path is taken exactly when the measured field is the file's digest. -/
theorem Cmb.redeploy_iff (c : Cmb) (env : Environment) (m : List (Nat × Nat)) (hin : c.dmp = env.dmp ∧ c.gravity = env.gravity) :
    c.plan env m = some .redeploy ↔ c.digest = m := by
  unfold Cmb.plan
  rw [if_pos hin]
  by_cases h : c.digest = m <;> simp [h]

/-- The Big Bang path is taken exactly when it is not. -/
theorem Cmb.bigBang_iff (c : Cmb) (env : Environment) (m : List (Nat × Nat)) (hin : c.dmp = env.dmp ∧ c.gravity = env.gravity) :
    c.plan env m = some .bigBang ↔ c.digest ≠ m := by
  unfold Cmb.plan
  rw [if_pos hin]
  by_cases h : c.digest = m <;> simp [h]

/-- On a sealed file the redeploy path means the live system spans the field of the file's recipe (T33): what is redeployed is the
    recipe the verdict is about, and the same environment verifies its own cold start by reproducing the same CMB. -/
theorem Cmb.redeploy_is_recipe_field (a1 a2 : Nat → Bool) (c : Cmb) (h : c.sealed (refBuild a1) (refBuild a2) = true)
    (env : Environment) (m : List (Nat × Nat)) (hr : c.plan env m = some .redeploy) : m = (Recipe.decode c.recipe).digest := by
  have hin := (c.plan_isSome_iff env m).mp (by rw [hr]; rfl)
  have hm := (c.redeploy_iff env m hin).mp hr
  rw [← hm, (c.sealed_verdict a1 a2 h).2.2]

/-- On a sealed, proven file the Big Bang path starts from a DMP-complete recipe: the mismatch selects the long path, it does not
    unprove the recipe. The plan for that path is the file itself. -/
theorem Cmb.bigBang_keeps_the_proof (a1 a2 : Nat → Bool) (c : Cmb) (h : c.sealed (refBuild a1) (refBuild a2) = true)
    (hp : c.verdict = .proven) (env : Environment) (m : List (Nat × Nat)) (_ : c.plan env m = some .bigBang) :
    (Recipe.decode c.recipe).dmpComplete c.classes a1 :=
  (c.sealed_proven_complete a1 a2 h hp).1

/-- Why the recipe must be in the file and not only its digest: a measured field that matches identifies no recipe (two proven recipes
    share a digest and decide differently), so a redeploy from a digest alone could restore a layout with other decisions. -/
theorem digest_alone_cannot_identify : ∃ r r' : Recipe, r.digest = r'.digest ∧ ∃ v i, r.decide v i ≠ r'.decide v i :=
  let ⟨r, r', _, _, hd, hv⟩ := digest_not_injective
  ⟨r, r', hd, hv⟩

end Dmp

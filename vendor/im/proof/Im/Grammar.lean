/-!
# The grammar: every sentence IM can speak, and nothing else

An IM sentence is one act (DMP, the words): a verb on a noun with its adjectives. The verb is one of three, read, write or execute
(the owner, 2026-10-26: "verbs reduce to read, write and execute over nouns and adjectives"). The noun is an item of an information
layer (DMP C12): the layer is one of a closed list, the path names the item inside it. The adjectives are the mode (dry run or apply,
DMP chapter 10 "Scripts an administrator runs") and what a write expects to find (C15: "absent for a create, a named value for an
overwrite"). The kind on the wire is `<layer>.<verb>` and is also the permission the token needs (DMP chapter 04, "Message kinds and
permissions": each kind needs exactly one permission).

The types are closed: a sentence that is not a term of them does not exist, so a theorem proved for every term is proved for every
sentence (the possibility space is the grammar, and only the grammar). `Sentence.all` lists every (kind, mode, expect) of the space;
the path is not enumerated (it is any legal name), so the theorems that depend on the path are proved for every path (Wire.lean).

**Prior art.** The three verbs over nouns are the rights alphabet of Lampson's access matrix ("Protection", Princeton, 1971): a token's
permission list is one row of it. A noun as a path in a namespace is Plan 9 (Pike, Presotto, Thompson, Trickey, Winterbottom, "The Use
of Name Spaces in Plan 9", 1992): every resource is a name, and control is a write to a control file, which is why a named verb people
type (pause, resume) is one write sentence here and not a fourth verb. The closed-types-and-generated-list shape is Gravity's
(`vendor/gravity/evidence/Dmp/Recipe.lean`, `Layouts.lean`).
-/
namespace Im

/-- The three verbs. read: information flows from the noun to the part; write: from the part to the noun; execute: the noun changes
    state and only the record flows (DMP Axiom 3). -/
inductive Verb where
  | read | write | execute
  deriving DecidableEq, Repr

/-- The information layers a noun can belong to (DMP C12: one layer per kind of information; the set of layers is open, so this list
    grows by addition at a stable promotion, C4). `setup` is the first: what a root repository needs to run itself (`vendor/dmp/SETUP.md`,
    the owner 2026-10-26: the repository that contains all information required to run it is the first direction for IM commands). -/
inductive Layer where
  | setup | board | file | record
  deriving DecidableEq, Repr

/-- The mode (DMP chapter 10): a dry run changes nothing and prints what it would do; apply does it. -/
inductive Mode where
  | dry | apply
  deriving DecidableEq, Repr

/-- What a write expects to find (C15): absent for a create, present for an overwrite; `any` is what a read or an execute says. -/
inductive Expect where
  | any | absent | present
  deriving DecidableEq, Repr

/-- A kind: a layer and a verb. It names the permission (DMP chapter 04: exactly one per kind). -/
structure Kind where
  layer : Layer
  verb : Verb
  deriving DecidableEq, Repr

/-- A name on the wire is bytes (DMP chapter 04, "Encoding": `id8` names are ASCII `a-z 0-9 . -`; a path adds `/`). -/
abbrev Name := List Nat

/-- A sentence: the kind, the path of the noun inside the layer, and the adjectives. -/
structure Sentence where
  kind : Kind
  path : Name
  mode : Mode
  expect : Expect
  deriving DecidableEq, Repr

def Verb.all : List Verb := [.read, .write, .execute]
def Layer.all : List Layer := [.setup, .board, .file, .record]
def Mode.all : List Mode := [.dry, .apply]
def Expect.all : List Expect := [.any, .absent, .present]
def Kind.all : List Kind := Layer.all.flatMap fun l => Verb.all.map fun v => ⟨l, v⟩

/-- The shape of a sentence without its path: everything the proof enumerates. -/
structure Shape where
  kind : Kind
  mode : Mode
  expect : Expect
  deriving DecidableEq, Repr

def Shape.all : List Shape := Kind.all.flatMap fun k => Mode.all.flatMap fun m => Expect.all.map fun e => ⟨k, m, e⟩
def Sentence.shape (s : Sentence) : Shape := ⟨s.kind, s.mode, s.expect⟩

theorem Verb.all_complete : ∀ v : Verb, v ∈ Verb.all := by intro v; cases v <;> decide
theorem Layer.all_complete : ∀ l : Layer, l ∈ Layer.all := by intro l; cases l <;> decide
theorem Mode.all_complete : ∀ m : Mode, m ∈ Mode.all := by intro m; cases m <;> decide
theorem Expect.all_complete : ∀ e : Expect, e ∈ Expect.all := by intro e; cases e <;> decide

theorem Kind.all_complete : ∀ k : Kind, k ∈ Kind.all := by
  intro ⟨l, v⟩
  unfold Kind.all
  simp only [List.mem_flatMap, List.mem_map]
  exact ⟨l, Layer.all_complete l, v, Verb.all_complete v, rfl⟩

/-- Every shape a sentence can have is in the list: the space is closed and the list is complete. -/
theorem Shape.all_complete : ∀ s : Shape, s ∈ Shape.all := by
  intro ⟨k, m, e⟩
  unfold Shape.all
  simp only [List.mem_flatMap, List.mem_map]
  exact ⟨k, Kind.all_complete k, m, Mode.all_complete m, e, Expect.all_complete e, rfl⟩

theorem Sentence.shape_mem (s : Sentence) : s.shape ∈ Shape.all := Shape.all_complete s.shape

/-! ## Names on the wire -/

def Verb.wire : Verb → Name
  | .read => [114, 101, 97, 100]
  | .write => [119, 114, 105, 116, 101]
  | .execute => [101, 120, 101, 99, 117, 116, 101]

def Layer.wire : Layer → Name
  | .setup => [115, 101, 116, 117, 112]
  | .board => [98, 111, 97, 114, 100]
  | .file => [102, 105, 108, 101]
  | .record => [114, 101, 99, 111, 114, 100]

/-- The kind on the wire and the permission it needs: `<layer>.<verb>`. -/
def Kind.wire (k : Kind) : Name := k.layer.wire ++ [46] ++ k.verb.wire

/-- A byte of a name: `a-z 0-9 . -`. -/
def nameByte (b : Nat) : Bool := (97 ≤ b && b ≤ 122) || (48 ≤ b && b ≤ 57) || b == 46 || b == 45

/-- A byte of a path: a name byte or `/`. -/
def pathByte (b : Nat) : Bool := nameByte b || b == 47

/-- A legal name: 1 to 64 name bytes (DMP chapter 04, limits of version 0, kept). -/
def nameOk (n : Name) : Bool := n.all nameByte && 1 ≤ n.length && n.length ≤ 64

/-- A legal path: 1 to 1024 path bytes. -/
def pathOk (p : Name) : Bool := p.all pathByte && 1 ≤ p.length && p.length ≤ 1024

/-- A sentence is well formed when its path is legal and a write says what it expects (C15) while a read or an execute says `any`. -/
def Sentence.wfB (s : Sentence) : Bool :=
  pathOk s.path &&
  (match s.kind.verb, s.expect with
   | .write, .any => false
   | .write, _ => true
   | _, .any => true
   | _, _ => false)

def Shape.wfB (sh : Shape) : Bool :=
  match sh.kind.verb, sh.expect with
  | .write, .any => false
  | .write, _ => true
  | _, .any => true
  | _, _ => false

theorem Sentence.wfB_shape (s : Sentence) : s.wfB = (pathOk s.path && s.shape.wfB) := rfl

/-- The well-formed shapes: what a door ever sees. -/
def Shape.allWf : List Shape := Shape.all.filter Shape.wfB

theorem Shape.allWf_complete (sh : Shape) (h : sh.wfB = true) : sh ∈ Shape.allWf :=
  List.mem_filter.mpr ⟨Shape.all_complete sh, h⟩

end Im

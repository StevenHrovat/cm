import Dmp.Complete

/-!
# Worked layouts: Gravity's verdict on five configurations

How a configuration is checked: write its recipe as a term, state the verdict, and let the kernel compute it (`rfl`). The run of
`tools/check.sh` is the gate. Failure classes here: `person` (always), host `1`, key `2`. A witness is written by the members it belongs
to; two people, two hosts and two keys make a minted Duality.
-/

namespace Dmp.Layouts

open Dmp

def host : FailureClass := 1
def key : FailureClass := 2
def classes : List FailureClass := [host, key]

/-- A witness that traces to person `p` on host `h` with key `k`. -/
def w (p h k : Nat) : Witness := ⟨fun c => if c = person then p else if c = host then h else if c = key then k else 0⟩

/-- A galaxy minted by two people on two hosts with two keys. -/
def galaxyDot : DoT := ⟨w 1 10 100, w 2 20 200⟩
/-- The knowledge layer's Duality: management (person 3) and the subject-matter expert (person 4), T38. -/
def knowledgeDot : DoT := ⟨w 3 30 300, w 4 40 400⟩
/-- A dwarf galaxy's Duality: two other people. -/
def dwarfDot : DoT := ⟨w 5 50 500, w 6 60 600⟩
/-- One part answering for both halves of a layer (the same person, host and key). -/
def onePartDot : DoT := ⟨w 7 70 700, w 7 70 700⟩

def line (kind : Kind) (dot : DoT) (parent : Option Nat) (cluster : Nat := 0) : Line := ⟨kind, dot, parent, cluster⟩

/-- Every line is carried by both chains. -/
def allAgree : Nat → Bool := fun _ => true

/-- A. A galaxy, a knowledge layer over it, a three-node privacy singularity in the knowledge layer, a dwarf galaxy under the galaxy. -/
def layoutA : Recipe :=
  { n := 4
    line := fun
      | 0 => line .galaxy galaxyDot none
      | 1 => line .information knowledgeDot (some 0)
      | 2 => line .singularity knowledgeDot (some 1) 3
      | _ => line .dwarf dwarfDot (some 0) }

example : layoutA.verdict classes allAgree = .proven := rfl

/-- B. The same, but the galaxy's two halves trace to one person: a pressed root (G0, T-P). -/
def layoutB : Recipe := { layoutA with line := fun i => if i = 0 then line .galaxy onePartDot none else layoutA.line i }

example : layoutB.verdict classes allAgree = .outOfSpec := rfl

/-- C. The same as A, but the singularity has two nodes (T3a's floor is three). -/
def layoutC : Recipe := { layoutA with line := fun i => if i = 2 then line .singularity knowledgeDot (some 1) 2 else layoutA.line i }

example : layoutC.verdict classes allAgree = .outOfSpec := rfl

/-- D. The knowledge layer's two halves are one part, laid over the minted galaxy: T38 allows it, the galaxy gates the layer. -/
def layoutD : Recipe := { layoutA with line := fun i => if i = 1 then line .information onePartDot (some 0) else layoutA.line i }

example : layoutD.verdict classes allAgree = .proven := rfl

/-- D'. The same one-part layer with nothing above it is refused: nobody gates it. -/
def layoutD' : Recipe := { n := 1, line := fun _ => line .galaxy onePartDot none }

example : layoutD'.verdict classes allAgree = .outOfSpec := rfl

/-- E. Layout A with the dwarf galaxy's components not yet carried by both chains: unproven, not out of spec. -/
example : layoutA.verdict classes (fun i => i != 3) = .unproven := rfl

/-- The galaxy's two halves on one shared host would not be a Duality under these classes, whatever their persons. -/
example : ¬ (DoT.mk (w 1 10 100) (w 2 10 200)).minted classes :=
  fun h => h host (by decide) rfl

/-- The digest of layout A: the galaxy at depth 0 with mass 4 (every line derives through it), the knowledge layer at depth 1 with
    mass 2, the singularity at depth 2 with mass 1, the dwarf at depth 1 with mass 1. -/
example : layoutA.digest = [(0, 4), (1, 2), (2, 1), (1, 1)] := rfl

end Dmp.Layouts

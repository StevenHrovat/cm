-- Prints the table of every well-formed shape with its wire form (proof/table/shapes.tsv): kind, permission, mode, expect, the sample
-- sentence's payload bytes in hex. The C# tests replay it (tests/Language/ShapeTableTests.cs); proof/tools/check.sh regenerates it and
-- fails when the committed file differs. The table is data neither proof chain owns.
import Im
namespace Im

def hex (b : Nat) : String :=
  let d := "0123456789abcdef".toList
  String.ofList [d[b / 16]!, d[b % 16]!]

def ascii (n : Name) : String := String.ofList (n.map fun b => Char.ofNat b)

def Verb.name : Verb → String | .read => "read" | .write => "write" | .execute => "execute"
def Layer.name : Layer → String | .setup => "setup" | .board => "board" | .file => "file" | .record => "record"
def Mode.name : Mode → String | .dry => "dry" | .apply => "apply"
def Expect.name : Expect → String | .any => "any" | .absent => "absent" | .present => "present"

def printTable : IO Unit := do
  IO.println "# kind\tpermission\tmode\texpect\tpayload-hex (path \"a\")"
  for sh in Shape.allWf do
    let bytes := sh.sample.encode
    IO.println s!"{sh.kind.layer.name}.{sh.kind.verb.name}\t{ascii sh.kind.wire}\t{sh.mode.name}\t{sh.expect.name}\t{String.join (bytes.map hex)}"

end Im

def main : IO Unit := Im.printTable

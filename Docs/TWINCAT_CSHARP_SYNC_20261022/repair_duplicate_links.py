from pathlib import Path
import re, codecs, hashlib
root=Path(r"E:\Orca\DATN")
p=root/r"PLC\TwinCAT\DATN\DATN.tsproj"
g=root/r"PLC\TwinCAT\DATN\PLC1\GVLs\GVL_MD200_IO.TcGVL"
back=root/r"Docs\TWINCAT_CSHARP_SYNC_20261022\BACKUP_PRECHANGE\DATN.tsproj_AFTER_XAE_AUTOLINK.xml"
if not back.exists(): back.write_bytes(p.read_bytes())
def read(path):
    b=path.read_bytes()
    return b.decode("utf-8-sig"), b.startswith(codecs.BOM_UTF8), "\r\n" if b"\r\n" in b else "\n"
def put(path, txt, bom, eol):
    raw=(codecs.BOM_UTF8 if bom else b"")+txt.replace("\r\n","\n").replace("\n",eol).encode("utf-8")
    path.write_bytes(raw)
t,bom,eol=read(p)
start=t.index("<Mappings>")
end=t.index("</Mappings>",start)
head,block,tail=t[:start],t[start:end],t[end:]
for name,part in [("bEStopFeedback","Inputs"),("bRUN_DO","Outputs")]:
    prefix="PlcTask "+part+"^GVL_MD200_IO."+name
    found=list(re.finditer(r'(?m)^[ \t]*<Link [^\r\n]*?VarA="'+re.escape(prefix)+r'"[^\r\n]*?/>[ \t]*\r?\n',block))
    if len(found)!=3: raise RuntimeError(f"Expected three XAE generated duplicates for {name}: {len(found)}")
    entries=[f.group(0) for f in found]
    destinations=set(re.findall(r'VarB="([^"]+)"', "\n".join(entries)))
    if len(destinations)!=1 or "Module " not in next(iter(destinations)):
        raise RuntimeError(f"Destination ambiguous {name}: {destinations}")
    # Retain one explicit bit-limited manual link, drop the duplicate autolinks.
    retained=re.sub(r'\s+AutoLink="true"','',entries[0])
    retained=re.sub(r'\s+Size="[^"]+"','',retained)
    retained=retained.replace("/>"," Size=\"1\"/>",1)
    block=block.replace(entries[0],retained,1)
    for entry in entries[1:]:
        if entry not in block: raise RuntimeError("expected duplicate missing")
        block=block.replace(entry,"",1)
t=head+block+tail
put(p,t,bom,eol)
s,gb,ge=read(g)
old_lines=re.findall(r"(?m)^.*\{attribute 'TcLinkTo(?:OSO)?' := '[^\r\n]*\}\r?\n",s)
if len(old_lines)!=3: raise RuntimeError(f"expected 3 legacy autolink attributes, found {len(old_lines)}")
for line in old_lines: s=s.replace(line,"",1)
put(g,s,gb,ge)
import xml.etree.ElementTree as ET
r=ET.fromstring(t)
links=r.findall(".//Mappings//Link")
assert len(links)==43, ("Total active links",len(links))
for name,part in [("bEStopFeedback","Inputs"),("bRUN_DO","Outputs")]:
    matches=[x for x in links if x.get("VarA")==f"PlcTask {part}^GVL_MD200_IO.{name}"]
    assert len(matches)==1 and matches[0].get("Size")=="1",name
assert "TcLinkTo" not in s
print("FIXED_DUPLICATE_LINKS=4 TOTAL_ACTIVE=43 PLC_IO_ACTIVE=23 LIVE_AUTOLINK_ATTRIBUTES=0")
print("TSHASH="+hashlib.sha256(p.read_bytes()).hexdigest())

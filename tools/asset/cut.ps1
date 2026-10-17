param(
  [string]$Sheet,
  [string]$Mode = "preview",      # preview | export
  [string]$Preview = "",          # preview png path
  [string]$Names = "",            # comma list in sorted order, "-" = skip
  [string]$OutDir = "",
  [double]$Scale = 0.6,
  [int]$MergeDist = 40,
  [double]$MinFrac = 0.02,
  [string]$Rects = "",           # "x0,y0,x1,y1;..." : group by rectangles instead of auto rows
  [int]$Alpha = 40,             # pixels above this alpha count as picture (raise to split pictures joined by a soft glow)
  [string]$Seeds = "",          # "x,y;..." one point inside each picture: splits pictures whose outlines touch (names follow seed order)
  [switch]$Raw                   # with -Rects: cut the rectangle itself (pictures that touch but do not overlap)
)
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @"
using System; using System.Collections.Generic; using System.Drawing; using System.Drawing.Imaging; using System.Linq;
public class Comp { public int Id, MinX=int.MaxValue, MinY=int.MaxValue, MaxX=-1, MaxY=-1, Area; public List<int> Members = new List<int>();
  public double Cx { get { return (MinX+MaxX)/2.0; } } public double Cy { get { return (MinY+MaxY)/2.0; } } public double OwnCx, OwnCy; }
public static class Cutter {
  public static int W, H; public static byte[] Px; public static int[] Label; public static List<Comp> Groups; public static List<Comp> All; public static int Thr = 40;
  public static void Load(string path) {
    using (var b = new Bitmap(path)) { W=b.Width; H=b.Height;
      var d = b.LockBits(new Rectangle(0,0,W,H), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
      Px = new byte[W*H*4]; System.Runtime.InteropServices.Marshal.Copy(d.Scan0, Px, 0, Px.Length); b.UnlockBits(d); }
  }
  public static string Segment(int mergeDist, double minFrac) {
    Label = new int[W*H]; var comps = new List<Comp>(); var q = new Queue<int>();
    for (int i=0;i<W*H;i++) { if (Label[i]!=0 || Px[i*4+3]<=Thr) continue;
      var c = new Comp{Id=comps.Count+1}; comps.Add(c); Label[i]=c.Id; q.Enqueue(i);
      while (q.Count>0) { int p=q.Dequeue(); int x=p%W, y=p/W; c.Area++;
        if(x<c.MinX)c.MinX=x; if(x>c.MaxX)c.MaxX=x; if(y<c.MinY)c.MinY=y; if(y>c.MaxY)c.MaxY=y;
        for(int dy=-1;dy<=1;dy++)for(int dx=-1;dx<=1;dx++){int nx=x+dx,ny=y+dy; if(nx<0||ny<0||nx>=W||ny>=H)continue; int n=ny*W+nx;
          if(Label[n]==0 && Px[n*4+3]>Thr){Label[n]=c.Id;q.Enqueue(n);} } } }
    foreach (var c in comps) { c.OwnCx=c.Cx; c.OwnCy=c.Cy; }
    All = comps; int maxA = comps.Count==0?0:comps.Max(c=>c.Area);
    var big = comps.Where(c=>c.Area>=maxA*minFrac).ToList();
    foreach (var c in big) c.Members.Add(c.Id);
    foreach (var c in comps.Where(c=>c.Area<maxA*minFrac)) {
      Comp best=null; double bd=double.MaxValue;
      foreach (var g in big) { double dx=Math.Max(0,Math.Max(g.MinX-c.MaxX,c.MinX-g.MaxX)), dy=Math.Max(0,Math.Max(g.MinY-c.MaxY,c.MinY-g.MaxY)); double d=Math.Sqrt(dx*dx+dy*dy); if(d<bd){bd=d;best=g;} }
      if (best!=null && bd<=mergeDist) { best.Members.Add(c.Id); best.MinX=Math.Min(best.MinX,c.MinX); best.MinY=Math.Min(best.MinY,c.MinY); best.MaxX=Math.Max(best.MaxX,c.MaxX); best.MaxY=Math.Max(best.MaxY,c.MaxY); }
    }
    // sort into rows
    var byY = big.OrderBy(g=>g.Cy).ToList(); var rows = new List<List<Comp>>();
    foreach (var g in byY) { var last = rows.LastOrDefault();
      if (last!=null && g.Cy - last.Average(r=>r.Cy) < 0.5*last.Average(r=>r.MaxY-r.MinY)) last.Add(g); else rows.Add(new List<Comp>{g}); }
    Groups = rows.SelectMany(r=>r.OrderBy(g=>g.Cx)).ToList();
    var sb = new System.Text.StringBuilder(); int k=0;
    foreach (var g in Groups) sb.AppendLine(string.Format("{0}: x{1}-{2} y{3}-{4} ({5}x{6})", k++, g.MinX,g.MaxX,g.MinY,g.MaxY,g.MaxX-g.MinX+1,g.MaxY-g.MinY+1));
    return sb.ToString();
  }
  // Replace the groups with one group per rectangle "x0,y0,x1,y1;..." holding every component whose center is inside it
  public static string GroupByRects(string rects, bool raw) {
    Groups = new List<Comp>(); var sb = new System.Text.StringBuilder(); int k=0;
    foreach (var r in rects.Split(new[]{';'}, StringSplitOptions.RemoveEmptyEntries)) { var v = r.Split(',').Select(int.Parse).ToArray(); var g = new Comp();
      if (raw) { var ids = new HashSet<int>(); for (int y=Math.Max(0,v[1]); y<=Math.Min(H-1,v[3]); y++) for (int x=Math.Max(0,v[0]); x<=Math.Min(W-1,v[2]); x++) { int L=Label[y*W+x]; if (L==0) continue; ids.Add(L); if(x<g.MinX)g.MinX=x; if(x>g.MaxX)g.MaxX=x; if(y<g.MinY)g.MinY=y; if(y>g.MaxY)g.MaxY=y; } g.Members.AddRange(ids); }
      else
      {
        // Segment() grows a big component's box when it absorbs small ones, so measure each component's own pixels here
        var ids = new HashSet<int>(All.Where(c => c.OwnCx>=v[0] && c.OwnCx<=v[2] && c.OwnCy>=v[1] && c.OwnCy<=v[3]).Select(c => c.Id)); g.Members.AddRange(ids);
        for (int i=0;i<W*H;i++) { if (!ids.Contains(Label[i])) continue; int x=i%W, y=i/W; if(x<g.MinX)g.MinX=x; if(x>g.MaxX)g.MaxX=x; if(y<g.MinY)g.MinY=y; if(y>g.MaxY)g.MaxY=y; }
      }
      Groups.Add(g); sb.AppendLine(string.Format("{0}: x{1}-{2} y{3}-{4} ({5} parts)", k++, g.MinX,g.MaxX,g.MinY,g.MaxY,g.Members.Count)); }
    return sb.ToString();
  }
  // Split pictures whose outlines touch: grow all seed points "x,y;..." through the picture pixels at once
  // (multi-source BFS); each pixel joins the seed that reaches it first. Groups follow seed order.
  public static string SeedSplit(string seeds) {
    var pts = seeds.Split(new[]{';'}, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Split(',').Select(int.Parse).ToArray()).ToList();
    var lab = new int[W*H]; var q = new Queue<int>(); var sb = new System.Text.StringBuilder();
    for (int k=0;k<pts.Count;k++) { int p = pts[k][1]*W+pts[k][0]; if (Px[p*4+3]>Thr) { lab[p]=k+1; q.Enqueue(p); } else sb.AppendLine("seed " + k + " is on a transparent pixel"); }
    while (q.Count>0) { int p=q.Dequeue(); int x=p%W, y=p/W;
      for(int dy=-1;dy<=1;dy++)for(int dx=-1;dx<=1;dx++){int nx=x+dx,ny=y+dy; if(nx<0||ny<0||nx>=W||ny>=H)continue; int n=ny*W+nx;
        if(lab[n]==0 && Px[n*4+3]>Thr){lab[n]=lab[p];q.Enqueue(n);} } }
    Label = lab; Groups = new List<Comp>();
    for (int k=0;k<pts.Count;k++) { var g=new Comp{Id=k+1}; g.Members.Add(k+1); Groups.Add(g); }
    for (int i=0;i<W*H;i++) { if (lab[i]==0) continue; var g=Groups[lab[i]-1]; int x=i%W, y=i/W;
      if(x<g.MinX)g.MinX=x; if(x>g.MaxX)g.MaxX=x; if(y<g.MinY)g.MinY=y; if(y>g.MaxY)g.MaxY=y; }
    int j=0; foreach (var g in Groups) sb.AppendLine(string.Format("{0}: x{1}-{2} y{3}-{4}", j++, g.MinX,g.MaxX,g.MinY,g.MaxY));
    return sb.ToString();
  }
  public static void PreviewImg(string path) {
    using (var b = new Bitmap(W,H,PixelFormat.Format32bppArgb)) {
      var d=b.LockBits(new Rectangle(0,0,W,H),ImageLockMode.WriteOnly,PixelFormat.Format32bppArgb); System.Runtime.InteropServices.Marshal.Copy(Px,0,d.Scan0,Px.Length); b.UnlockBits(d);
      using (var o = new Bitmap(W,H)) using (var g = Graphics.FromImage(o)) { g.Clear(Color.FromArgb(255,120,120,130)); g.DrawImage(b,0,0);
        using (var pen=new Pen(Color.Red,3)) using (var f=new Font("Arial",36,FontStyle.Bold)) { int k=0;
          foreach (var c in Groups) { g.DrawRectangle(pen,c.MinX,c.MinY,c.MaxX-c.MinX,c.MaxY-c.MinY); g.DrawString((k++).ToString(),f,Brushes.Yellow,c.MinX+4,c.MinY+4);} }
        o.Save(path, ImageFormat.Png); } }
  }
  public static string Export(string[] names, string outDir, double scale) {
    var sb = new System.Text.StringBuilder();
    for (int k=0;k<Groups.Count && k<names.Length;k++) { string name=names[k].Trim(); if (name=="-"||name=="") continue;
      var g=Groups[k]; var mem=new HashSet<int>(g.Members); int pad=4;
      int x0=Math.Max(0,g.MinX-pad), y0=Math.Max(0,g.MinY-pad), x1=Math.Min(W-1,g.MaxX+pad), y1=Math.Min(H-1,g.MaxY+pad); int cw=x1-x0+1, ch=y1-y0+1;
      var keep = new bool[cw*ch];
      for(int y=y0;y<=y1;y++)for(int x=x0;x<=x1;x++){ if(!mem.Contains(Label[y*W+x]))continue;
        for(int dy=-3;dy<=3;dy++)for(int dx=-3;dx<=3;dx++){int nx=x+dx-x0,ny=y+dy-y0; if(nx>=0&&ny>=0&&nx<cw&&ny<ch){ int L=Label[(ny+y0)*W+(nx+x0)]; if(L==0||mem.Contains(L)) keep[ny*cw+nx]=true; } } }
      var buf=new byte[cw*ch*4];
      for(int y=0;y<ch;y++)for(int x=0;x<cw;x++){ if(!keep[y*cw+x])continue; int s=((y+y0)*W+(x+x0))*4, t=(y*cw+x)*4; for(int i=0;i<4;i++)buf[t+i]=Px[s+i]; }
      using (var crop=new Bitmap(cw,ch,PixelFormat.Format32bppArgb)) {
        var d=crop.LockBits(new Rectangle(0,0,cw,ch),ImageLockMode.WriteOnly,PixelFormat.Format32bppArgb); System.Runtime.InteropServices.Marshal.Copy(buf,0,d.Scan0,buf.Length); crop.UnlockBits(d);
        int ow=Math.Max(1,(int)Math.Round(cw*scale)), oh=Math.Max(1,(int)Math.Round(ch*scale));
        using (var o=new Bitmap(ow,oh,PixelFormat.Format32bppArgb)) using (var gr=Graphics.FromImage(o)) {
          gr.InterpolationMode=System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic; gr.PixelOffsetMode=System.Drawing.Drawing2D.PixelOffsetMode.HighQuality; gr.CompositingMode=System.Drawing.Drawing2D.CompositingMode.SourceCopy;
          using (var ia=new ImageAttributes()) { ia.SetWrapMode(System.Drawing.Drawing2D.WrapMode.TileFlipXY); gr.DrawImage(crop,new Rectangle(0,0,ow,oh),0,0,cw,ch,GraphicsUnit.Pixel,ia); }
          string p=System.IO.Path.Combine(outDir,name+".png"); System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(p)); o.Save(p,ImageFormat.Png);
          sb.AppendLine(string.Format("{0} -> {1} {2}x{3}", k, name, ow, oh)); } } }
    return sb.ToString();
  }
}
"@
[Cutter]::Load($Sheet)
[Cutter]::Thr = $Alpha
$seg = [Cutter]::Segment($MergeDist, $MinFrac)
if ($Seeds) { [Cutter]::SeedSplit($Seeds) } elseif ($Rects) { [Cutter]::GroupByRects($Rects, [bool]$Raw) } else { $seg }
if ($Mode -eq "preview" -and $Preview) { [Cutter]::PreviewImg($Preview) }
if ($Mode -eq "export") { [Cutter]::Export($Names.Split(','), $OutDir, $Scale) }

using System.Reflection;
using System.Text.Json;
using System.Net;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using CRMRSDesktopApp;
class Program {
const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
static void Check(bool ok, string text) { if (!ok) throw new Exception(text); Console.WriteLine("PASS " + text); }
[STAThread] static void Main() {
var assembly=typeof(App).Assembly;
var dto=assembly.GetType("CRMRSDesktopApp.Data.DesktopApiClient+RepoSubmissionDto")!;
var src=JsonSerializer.Deserialize("{\"Id\":1,\"RepoCharges\":1000,\"Advance\":100,\"CashAmount\":200,\"TotalGross\":1200,\"AddlChargesAmount\":200}",dto)!;
var rowType=assembly.GetType("CRMRSDesktopApp.Accounts.AccountsPage+AcctRow")!;
var row=rowType.GetMethod("From",All)!.Invoke(null,new[]{src})!;
object Get(string n)=>rowType.GetProperty(n,All)!.GetValue(row)!;
Check((string)Get("FinalText")=="700","initial seizing calculation");
rowType.GetProperty("AdvanceText")!.SetValue(row,"250");
rowType.GetProperty("CashText")!.SetValue(row,"300");
Check((string)Get("FinalText")=="450","edited amounts recalculate seizing charges");
Check((decimal)dto.GetProperty("Advance")!.GetValue(Get("Src"))! ==250,"agent bill snapshot contains edited advance");
Check((decimal)dto.GetProperty("CashAmount")!.GetValue(Get("Src"))! ==300,"agent bill snapshot contains edited cash");
rowType.GetProperty("CashText")!.SetValue(row,"");
Check((decimal)dto.GetProperty("CashAmount")!.GetValue(Get("Src"))! ==0,"clearing cash persists zero");
var app=new Application();
foreach(var resource in new[]{"Colors","Styles","ExcelGrid"}) app.Resources.MergedDictionaries.Add(new ResourceDictionary{Source=new Uri("/CRMRS;component/Material/"+resource+".xaml",UriKind.Relative)});
app.Resources["Accent"]=System.Windows.Media.Brushes.Orange;
var page=new CRMRSDesktopApp.Accounts.AccountsPage();
var grid=(DataGrid)page.FindName("grid");
var expected="Vehicle No|Customer Name|Repo Date|Model|Finance|Yard Name|Inventory|Inventory Remark|Billing Status|Billing Remark|Collection Update|Addln Amount|Repo Charges|Gross Amount|Remark|Cash Collected|Advance|Seizing Charges";
Check(string.Join("|",grid.Columns.Select(c=>c.Header))==expected,"exact Accounts column order");
grid.Measure(new Size(1000,600)); grid.Arrange(new Rect(0,0,1000,600)); grid.UpdateLayout(); Check(grid.FrozenColumnCount==2,"two frozen identity columns");
Check(grid.Columns.OfType<DataGridTextColumn>().Where(c=>!c.IsReadOnly).All(c=>((Binding)c.Binding).UpdateSourceTrigger==UpdateSourceTrigger.Explicit),"text changes do not update on focus loss");
var gate=assembly.GetType("CRMRSDesktopApp.EnterOnlyGridSave")!;
var ending=new DataGridCellEditEndingEventArgs(grid.Columns[1],new DataGridRow(),new TextBox(),DataGridEditAction.Commit);
Check(!(bool)gate.GetMethod("AllowCommit",All)!.Invoke(null,new object[]{grid,ending})! && ending.Cancel,"focus-loss commit blocked");
var handler=new Capture(); App.HttpClient=new HttpClient(handler);
var api=assembly.GetType("CRMRSDesktopApp.Data.DesktopApiClient")!;
((Task)api.GetMethod("MarkSubmissionBilledAsync",All)!.Invoke(null,new object[]{1L,0L,"TEST",null,null,1200m,"remark",1000m,200m})!).GetAwaiter().GetResult();
using var json=JsonDocument.Parse(handler.Body);
Check(json.RootElement.GetProperty("RepoCharges").GetDecimal()==1000 && json.RootElement.GetProperty("AddlChargesAmount").GetDecimal()==200 && json.RootElement.GetProperty("TotalGross").GetDecimal()==1200,"billing transmits all three amounts together");
Console.WriteLine("All regression checks passed; HTTP mocked, no live records changed.");
}
class Capture:HttpMessageHandler { public string Body=""; protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken c){Body=await r.Content!.ReadAsStringAsync();return new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("{}")};}}
}


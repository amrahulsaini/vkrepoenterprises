using System.Reflection;
using System.Text.Json;
using System.Net;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CRMRSDesktopApp;
class Program {
const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
static void Check(bool ok, string text) { if (!ok) throw new Exception(text); Console.WriteLine("PASS " + text); }
[STAThread] static void Main() {
var assembly=typeof(App).Assembly;
var dto=assembly.GetType("CRMRSDesktopApp.Data.DesktopApiClient+RepoSubmissionDto")!;
var src=JsonSerializer.Deserialize("{\"Id\":1,\"BillingAction\":\"immediate\",\"RepoCharges\":1000,\"Advance\":100,\"CashAmount\":200,\"TotalGross\":1200,\"AddlChargesAmount\":200}",dto)!;
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
var app=new Application { ShutdownMode=ShutdownMode.OnExplicitShutdown };
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
// Exercise a real WPF editor and its routed Enter event against mocked HTTP.
var window = new Window { Content = page, Width = 1400, Height = 800, Left = -10000, Top = -10000, ShowInTaskbar = false };
window.Show(); Pump();
var shown=(System.Collections.IList)page.GetType().GetField("_shown",All)!.GetValue(page)!;
shown.Add(row); grid.SelectedItem=row; grid.UpdateLayout();
handler.Saved=JsonSerializer.Deserialize<Dictionary<string,JsonElement>>(JsonSerializer.Serialize(Get("Src"),dto))!;
var before=handler.Posts;
grid.CurrentCell=new DataGridCellInfo(row,grid.Columns[1]);
grid.ScrollIntoView(row,grid.Columns[1]); grid.UpdateLayout(); grid.BeginEdit(); grid.UpdateLayout();
var editor=Child<TextBox>(grid)!;
Check(editor != null,"actual Accounts text editor opened");
editor.Text="ENTER SAVE TEST";
var key=new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(window),0,Key.Enter){RoutedEvent=Keyboard.PreviewKeyDownEvent};
editor.RaiseEvent(key); Pump();
Check(handler.Posts>before,"Accounts routed Enter sends save request");
Check(handler.Body.Contains("ENTER SAVE TEST"),"Accounts request includes edited value");
void Edit(int column, string value, string expectedField)
{
    grid.CurrentCell=new DataGridCellInfo(row,grid.Columns[column]);
    grid.ScrollIntoView(row,grid.Columns[column]); grid.UpdateLayout(); grid.BeginEdit(); grid.UpdateLayout();
    var box=Child<TextBox>(grid)!;
    var count=handler.Posts; box.Text=value;
    box.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(window),0,Key.Enter){RoutedEvent=Keyboard.PreviewKeyDownEvent}); Pump();
    Check(handler.Posts>count && handler.Body.Contains(expectedField),"Enter saves " + grid.Columns[column].Header + " to " + expectedField);
}
Edit(0,"TEST1234","VehicleNo"); Edit(2,"2026-09-12 10:00","CreatedAt");
Edit(3,"MODEL TEST","Model"); Edit(4,"FINANCE TEST","FinanceName"); Edit(5,"YARD TEST","ParkingYardName");
Edit(7,"Inventory note","InventoryRemark"); Edit(9,"Billing note","BillingRemark");
Edit(10,"Collection note","CollectionUpdate"); Edit(11,"250","AddlChargesAmount");
Edit(12,"1500","BillingRepoCharges"); Edit(13,"1700","TotalGross"); Edit(14,"Remark test","Remark");
Edit(15,"300","CashAmount"); Edit(16,"100","Advance"); Edit(17,"800","RepoCharges");
Check((decimal)dto.GetProperty("BillingRepoCharges")!.GetValue(Get("Src"))! ==1500 && (decimal)dto.GetProperty("RepoCharges")!.GetValue(Get("Src"))! ==800,"Billing repo and Accounts seizing stay independent");
Check((string)Get("FinalText")=="400","old seizing deduction behavior retained");
var right=(TextBox)page.FindName("txtAcRepo"); right.Text="900";
var rightBefore=handler.Posts;
right.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(window),0,Key.Enter){RoutedEvent=Keyboard.PreviewKeyDownEvent}); Pump();
Check(handler.Posts>rightBefore && handler.Body.Contains("900"),"right-side seizing Enter saves");
Check(grid.Columns.All(c=>!c.IsReadOnly),"all requested Accounts columns editable");
grid.CurrentCell=new DataGridCellInfo(row,grid.Columns[6]); grid.ScrollIntoView(row,grid.Columns[6]); grid.UpdateLayout(); grid.BeginEdit(); grid.UpdateLayout();
var picker=(ComboBox)grid.Columns[6].GetCellContent(row); picker.Focus(); Pump();
Console.WriteLine($"PICK loaded={picker.IsLoaded} focus={picker.IsKeyboardFocusWithin} selected={picker.SelectedItem}");
var pickBefore=handler.Posts; picker.SelectedItem="Yes"; Pump();
Console.WriteLine($"PICK posts={handler.Posts-pickBefore} body={handler.Body} value={Get("CourierYn")}");
Check(handler.Posts>pickBefore && handler.Body.Contains("CourierYn"),"inventory dropdown saves immediately without Enter");
grid.CurrentCell=new DataGridCellInfo(row,grid.Columns[3]); grid.ScrollIntoView(row,grid.Columns[3]); grid.UpdateLayout(); grid.BeginEdit(); grid.UpdateLayout();
var pending=Child<TextBox>(grid)!; pending.Text="FOCUS MOVED TEST"; grid.Focus();
var focusBefore=handler.Posts;
grid.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(window),0,Key.Enter){RoutedEvent=Keyboard.PreviewKeyDownEvent}); Pump();
Check(handler.Posts>focusBefore && handler.Body.Contains("FOCUS MOVED TEST"),"Enter saves pending editor after focus moves to grid");
window.Close();
var reloadTask=(Task)api.GetMethod("GetRepoSubmissionsAsync",All)!.Invoke(null,new object[]{null,null,new List<int>(),null})!;
reloadTask.GetAwaiter().GetResult();
var reloaded=(System.Collections.IList)reloadTask.GetType().GetProperty("Result")!.GetValue(reloadTask)!;
Check((decimal)dto.GetProperty("BillingRepoCharges")!.GetValue(reloaded[0])! ==1500 && (decimal)dto.GetProperty("RepoCharges")!.GetValue(reloaded[0])! ==900,"saved independent charges survive API reload");
handler.Saved.Clear();
void TestOtherGrid(FrameworkElement host, DataGrid other, object otherRow, string label)
{
    var owner=host as Window ?? new Window {Content=host,Width=1400,Height=800,Left=-10000,Top=-10000,ShowInTaskbar=false};
    owner.Left=-10000;owner.ShowInTaskbar=false;owner.Show();Pump();
    var array=Array.CreateInstance(otherRow.GetType(),1);array.SetValue(otherRow,0);other.ItemsSource=array;
    var column=other.Columns.OfType<DataGridTextColumn>().First(c=>((Binding)c.Binding).Path.Path=="CustomerName");
    other.SelectedItem=otherRow; other.CurrentCell=new DataGridCellInfo(otherRow,column);other.ScrollIntoView(otherRow,column);other.UpdateLayout();other.BeginEdit();other.UpdateLayout();
    Pump(); other.UpdateLayout(); var box=(TextBox)column.GetCellContent(otherRow);box.Text=label+" ENTER TEST";var count=handler.Posts;
    box.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(owner),0,Key.Enter){RoutedEvent=Keyboard.PreviewKeyDownEvent});Pump();
    Check(handler.Posts>count && handler.Body.Contains(label+" ENTER TEST"),label+" actual Enter sends edited field");owner.Close();handler.Saved.Clear();
}
var couriers=new CRMRSDesktopApp.Couriers.CouriersPage();
var courierRowType=assembly.GetType("CRMRSDesktopApp.Couriers.CouriersPage+Row")!;
var courierRow=Activator.CreateInstance(courierRowType,true)!;courierRowType.GetProperty("Src")!.SetValue(courierRow,Get("Src"));
TestOtherGrid(couriers,(DataGrid)couriers.FindName("grid"),courierRow,"Couriers");
var billing=new CRMRSDesktopApp.Billing.ViewAllDetailsWindow(null!,null,new List<int>());
var billingRowType=assembly.GetType("CRMRSDesktopApp.Billing.ViewAllDetailsWindow+Row")!;
var billingRow=billingRowType.GetMethod("From",All)!.Invoke(null,new[]{Get("Src")})!;
TestOtherGrid(billing,(DataGrid)billing.FindName("grid"),billingRow,"Billing");
Console.WriteLine("All regression checks passed; HTTP mocked, no live records changed.");
}
static void Pump() { var frame=new DispatcherFrame(); Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,new Action(()=>frame.Continue=false)); Dispatcher.PushFrame(frame); }
static T? Child<T>(DependencyObject root) where T:DependencyObject { for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var c=VisualTreeHelper.GetChild(root,i); if(c is T t)return t;var found=Child<T>(c);if(found!=null)return found;}return null;}
class Capture:HttpMessageHandler {
 public string Body=""; public int Posts; public Dictionary<string,JsonElement> Saved=new();
 protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken c){
  if(r.Method==HttpMethod.Post){Posts++;Body=await r.Content!.ReadAsStringAsync();
   if(r.RequestUri!.AbsolutePath.EndsWith("/fields") || r.RequestUri.AbsolutePath.EndsWith("/update"))
    foreach(var pair in JsonSerializer.Deserialize<Dictionary<string,JsonElement>>(Body)!) Saved[pair.Key]=pair.Value.Clone();
  }
  var body=r.Method==HttpMethod.Get ? (Saved.Count>0 ? JsonSerializer.Serialize(new[]{Saved}) : "[]") : "{}";
  return new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(body)};
 }
}
}

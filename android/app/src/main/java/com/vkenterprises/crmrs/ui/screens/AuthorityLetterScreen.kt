package com.vkenterprises.crmrs.ui.screens

import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.gestures.detectDragGestures
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.*
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.navigation.NavController
import coil.compose.AsyncImage
import com.vkenterprises.crmrs.BuildConfig
import com.vkenterprises.crmrs.data.api.ApiClient
import com.vkenterprises.crmrs.data.models.SearchResult
import com.vkenterprises.crmrs.data.models.StampPositionRequest
import com.vkenterprises.crmrs.ui.theme.RobotoFamily
import com.vkenterprises.crmrs.utils.AuthorityLetterPdf
import com.vkenterprises.crmrs.utils.RepoPdf
import com.vkenterprises.crmrs.viewmodel.AuthViewModel
import com.vkenterprises.crmrs.viewmodel.SearchViewModel
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import okhttp3.MediaType.Companion.toMediaTypeOrNull
import okhttp3.RequestBody.Companion.toRequestBody
import java.text.SimpleDateFormat
import java.util.Locale

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun AuthorityLetterScreen(
    searchVm: SearchViewModel,
    authVm: AuthViewModel,
    nav: NavController
) {
    val ui        by searchVm.ui.collectAsState()
    val isAdmin   by authVm.isAdmin.collectAsState(initial = false)
    val userId    by authVm.userId.collectAsState(initial = -1L)
    val agentName by authVm.userName.collectAsState(initial = "")
    val agentPhone by authVm.userMobile.collectAsState(initial = "")
    val context = LocalContext.current
    val scope   = rememberCoroutineScope()

    var query by remember { mutableStateOf("") }
    var busy  by remember { mutableStateOf(false) }
    var msg   by remember { mutableStateOf<String?>(null) }
    var lhBusy by remember { mutableStateOf(false) }
    var hasLetterhead by remember { mutableStateOf(false) }
    var hasWatermark  by remember { mutableStateOf(false) }
    var stampUrl      by remember { mutableStateOf<String?>(null) }
    var stampX by remember { mutableStateOf(380f) }
    var stampY by remember { mutableStateOf(690f) }
    var stampW by remember { mutableStateOf(150f) }
    var stampH by remember { mutableStateOf(80f) }
    var stampSaving by remember { mutableStateOf(false) }
    var validFrom by remember { mutableStateOf("") }
    var validTo   by remember { mutableStateOf("") }
    var execName  by remember { mutableStateOf("") }
    var execId    by remember { mutableStateOf("") }
    var pending   by remember { mutableStateOf<SearchResult?>(null) }
    var showSettings by remember { mutableStateOf(false) }

    fun absUrl(p: String): String {
        if (p.startsWith("http")) return p
        val rel = if (p.trimStart('/').startsWith("uploads/")) p.trimStart('/')
                  else "uploads/" + p.trimStart('/')
        return BuildConfig.BASE_URL.trimEnd('/') + "/" + rel
    }

    suspend fun refreshAgency() {
        runCatching { ApiClient.api.getAgencyInfo() }
            .getOrNull()?.takeIf { it.isSuccessful }?.body()?.let {
                hasLetterhead = it.letterheadPath.isNotBlank()
                hasWatermark  = it.watermarkPath.isNotBlank()
                stampUrl = it.stampPath.takeIf { p -> p.isNotBlank() }?.let { p -> absUrl(p) }
                stampX = it.stampX; stampY = it.stampY
                stampW = it.stampW; stampH = it.stampH
            }
    }

    fun saveStampPosition() {
        if (stampSaving) return
        stampSaving = true
        scope.launch {
            val ok = runCatching {
                ApiClient.api.saveStampPosition(
                    StampPositionRequest(stampX, stampY, stampW, stampH)
                ).isSuccessful
            }.getOrDefault(false)
            msg = if (ok) "Stamp position saved." else "Couldn't save the stamp position."
            stampSaving = false
        }
    }

    LaunchedEffect(Unit) {
        searchVm.clearLetterResults()
        refreshAgency()
    }

    var uploadKind by remember { mutableStateOf("letterhead") }
    val picker = rememberLauncherForActivityResult(ActivityResultContracts.GetContent()) { uri ->
        if (uri == null) return@rememberLauncherForActivityResult
        val kind = uploadKind
        lhBusy = true; msg = null
        scope.launch {
            val ok = runCatching {
                val bytes = withContext(Dispatchers.IO) {
                    context.contentResolver.openInputStream(uri)?.use { it.readBytes() }
                } ?: return@runCatching false
                val type = context.contentResolver.getType(uri) ?: "image/png"
                val ext  = if (type.contains("jp")) "jpg" else "png"
                val part = okhttp3.MultipartBody.Part.createFormData(
                    "file", "$kind.$ext", bytes.toRequestBody(type.toMediaTypeOrNull())
                )
                ApiClient.api.uploadLetterhead(part, kind).isSuccessful
            }.getOrDefault(false)
            msg = if (ok) "Saved to cloud." else "Upload failed. Use a PNG or JPG under 8 MB."
            if (ok) refreshAgency()
            lhBusy = false
        }
    }

    fun generate(rec: SearchResult) {
        if (busy) return
        busy = true; msg = null
        scope.launch {
            runCatching {
                val info = runCatching { ApiClient.api.getAgencyInfo() }
                    .getOrNull()?.takeIf { it.isSuccessful }?.body()
                val lh = info?.letterheadPath.orEmpty()
                val wm = info?.watermarkPath.orEmpty()
                val st = info?.stampPath.orEmpty()
                val bmp   = if (lh.isNotBlank()) RepoPdf.loadBitmap(absUrl(lh)) else null
                val wmBmp = if (wm.isNotBlank()) RepoPdf.loadBitmap(absUrl(wm)) else null
                val stBmp = if (st.isNotBlank()) RepoPdf.loadBitmap(absUrl(st)) else null
                val data = AuthorityLetterPdf.Data(
                    agencyName   = info?.name?.takeIf { it.isNotBlank() } ?: BuildConfig.AGENCY_NAME,
                    regNo        = "",
                    gstNo        = "",
                    dateText     = SimpleDateFormat("dd MMM yyyy", Locale.US).format(java.util.Date()),
                    bankNbfc     = rec.financer.ifBlank { rec.branchName },
                    loanAcNo     = rec.agreementNo,
                    borrowerName = rec.customerName,
                    vehicleNo    = rec.vehicleNo,
                    chassisNo    = rec.chassisNo,
                    engineNo     = rec.engineNo,
                    authorizedExecutive = execName.trim(),
                    executiveId  = execId.trim(),
                    validFrom    = validFrom.trim(),
                    validTo      = validTo.trim(),
                    letterhead   = bmp,
                    watermark    = wmBmp,
                    stamp        = stBmp,
                    stampX       = info?.stampX ?: stampX,
                    stampY       = info?.stampY ?: stampY,
                    stampW       = info?.stampW ?: stampW,
                    stampH       = info?.stampH ?: stampH
                )
                val file = withContext(Dispatchers.IO) { AuthorityLetterPdf.generate(context, data) }
                RepoPdf.open(context, file, "application/pdf")
            }.onFailure { msg = "Could not create the letter." }
            busy = false
        }
    }

    Scaffold(
        topBar = {
            TopAppBar(
                title = { Text("Authorization Letter", fontWeight = FontWeight.Bold) },
                navigationIcon = {
                    IconButton(onClick = { nav.popBackStack() }) {
                        Icon(Icons.Default.ArrowBack, "Back")
                    }
                },
                actions = {
                    if (isAdmin) {
                        IconButton(onClick = { showSettings = true }) {
                            Icon(Icons.Default.Settings, "Letterhead settings")
                        }
                    }
                },
                colors = TopAppBarDefaults.topAppBarColors(
                    containerColor = MaterialTheme.colorScheme.surface
                )
            )
        }
    ) { pad ->
        Column(Modifier.padding(pad).fillMaxSize().padding(16.dp)) {

            Text(
                "SEARCH VEHICLE",
                style = MaterialTheme.typography.labelSmall,
                fontWeight = FontWeight.Bold,
                letterSpacing = 1.2.sp,
                color = MaterialTheme.colorScheme.onSurfaceVariant
            )
            Spacer(Modifier.height(8.dp))
            OutlinedTextField(
                value = query,
                onValueChange = {
                    val digits = it.filter { c -> c.isDigit() }.take(4)
                    query = digits
                    if (digits.length == 4) searchVm.searchForLetter(digits, userId)
                    else searchVm.clearLetterResults()
                },
                placeholder = { Text("Last 4 digits of RC") },
                leadingIcon = { Icon(Icons.Default.Search, null) },
                textStyle = androidx.compose.ui.text.TextStyle(
                    fontFamily = RobotoFamily,
                    fontWeight = FontWeight.Bold,
                    fontSize = 18.sp,
                    letterSpacing = 2.sp
                ),
                keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
                singleLine = true,
                shape = RoundedCornerShape(10.dp),
                modifier = Modifier.fillMaxWidth()
            )

            msg?.let {
                Spacer(Modifier.height(8.dp))
                Text(it, style = MaterialTheme.typography.labelSmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant)
            }

            Spacer(Modifier.height(10.dp))

            when {
                ui.letterSearching -> Row(
                    Modifier.fillMaxWidth().padding(16.dp),
                    horizontalArrangement = Arrangement.Center,
                    verticalAlignment = Alignment.CenterVertically
                ) {
                    CircularProgressIndicator(Modifier.size(18.dp), strokeWidth = 2.dp)
                    Spacer(Modifier.width(10.dp))
                    Text("Searching…", style = MaterialTheme.typography.bodySmall)
                }
                query.length == 4 && ui.letterResults.isEmpty() -> Text(
                    "No vehicle found for those digits.",
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                    modifier = Modifier.padding(top = 12.dp)
                )
                else -> LazyColumn(modifier = Modifier.fillMaxSize()) {
                    items(ui.letterResults, key = { it.id }) { rec ->
                        Row(
                            Modifier
                                .fillMaxWidth()
                                .clickable { pending = rec }
                                .padding(horizontal = 6.dp, vertical = 8.dp),
                            verticalAlignment = Alignment.CenterVertically
                        ) {
                            Text(
                                rec.vehicleNo.ifBlank { "—" },
                                fontWeight = FontWeight.Black,
                                fontFamily = RobotoFamily,
                                fontSize = 17.sp,
                                maxLines = 1,
                                modifier = Modifier.weight(1f)
                            )
                            Text(
                                rec.model.ifBlank { "—" },
                                style = MaterialTheme.typography.bodySmall,
                                fontFamily = RobotoFamily,
                                fontWeight = FontWeight.Bold,
                                maxLines = 1,
                                color = MaterialTheme.colorScheme.onSurfaceVariant,
                                modifier = Modifier.weight(1f)
                            )
                            Icon(
                                Icons.Default.Download, null,
                                tint = MaterialTheme.colorScheme.primary,
                                modifier = Modifier.size(17.dp)
                            )
                        }
                        HorizontalDivider(
                            color = MaterialTheme.colorScheme.outlineVariant.copy(alpha = 0.3f),
                            thickness = 0.5.dp
                        )
                    }
                }
            }

            if (busy) {
                Spacer(Modifier.height(10.dp))
                Row(verticalAlignment = Alignment.CenterVertically) {
                    CircularProgressIndicator(Modifier.size(16.dp), strokeWidth = 2.dp)
                    Spacer(Modifier.width(8.dp))
                    Text("Preparing letter…", style = MaterialTheme.typography.bodySmall)
                }
            }
        }
    }

    if (showSettings && isAdmin) {
        AlertDialog(
            onDismissRequest = { showSettings = false },
            confirmButton = {
                TextButton(onClick = { showSettings = false }) { Text("Done") }
            },
            title = { Text("Letter settings", fontWeight = FontWeight.Bold) },
            text = {
                Column {

                        Row(
                            verticalAlignment = Alignment.CenterVertically,
                            horizontalArrangement = Arrangement.spacedBy(8.dp)
                        ) {
                            Icon(
                                if (hasLetterhead) Icons.Default.CheckCircle else Icons.Default.Upload,
                                null,
                                tint = if (hasLetterhead) Color(0xFF388E3C)
                                       else MaterialTheme.colorScheme.primary,
                                modifier = Modifier.size(18.dp)
                            )
                            Text(
                                if (hasLetterhead) "LETTERHEAD UPLOADED" else "LETTERHEAD NOT UPLOADED",
                                style = MaterialTheme.typography.labelLarge,
                                fontWeight = FontWeight.Bold,
                                color = if (hasLetterhead) Color(0xFF388E3C)
                                        else MaterialTheme.colorScheme.onSurfaceVariant
                            )
                        }
                        Spacer(Modifier.height(6.dp))
                        Text(
                            "Printed at the top of every letter.",
                            style = MaterialTheme.typography.bodySmall,
                            color = MaterialTheme.colorScheme.onSurfaceVariant
                        )
                        Spacer(Modifier.height(10.dp))
                        Button(
                            onClick = { uploadKind = "letterhead"; picker.launch("image/*") },
                            enabled = !lhBusy,
                            modifier = Modifier.fillMaxWidth()
                        ) {
                            Icon(Icons.Default.Upload, null, Modifier.size(18.dp))
                            Spacer(Modifier.width(6.dp))
                            Text(
                                if (lhBusy) "Uploading…"
                                else if (hasLetterhead) "Replace Letterhead"
                                else "Upload Letterhead"
                            )
                        }
                        Spacer(Modifier.height(10.dp))
                        HorizontalDivider()
                        Spacer(Modifier.height(10.dp))
                        Row(
                            verticalAlignment = Alignment.CenterVertically,
                            horizontalArrangement = Arrangement.spacedBy(8.dp)
                        ) {
                            Icon(
                                if (hasWatermark) Icons.Default.CheckCircle else Icons.Default.Image,
                                null,
                                tint = if (hasWatermark) Color(0xFF388E3C)
                                       else MaterialTheme.colorScheme.primary,
                                modifier = Modifier.size(18.dp)
                            )
                            Text(
                                if (hasWatermark) "BACKGROUND UPLOADED" else "BACKGROUND NOT UPLOADED",
                                style = MaterialTheme.typography.labelLarge,
                                fontWeight = FontWeight.Bold,
                                color = if (hasWatermark) Color(0xFF388E3C)
                                        else MaterialTheme.colorScheme.onSurfaceVariant
                            )
                        }
                        Spacer(Modifier.height(6.dp))
                        Text(
                            "Printed faintly behind the letter text.",
                            style = MaterialTheme.typography.bodySmall,
                            color = MaterialTheme.colorScheme.onSurfaceVariant
                        )
                        Spacer(Modifier.height(10.dp))
                        OutlinedButton(
                            onClick = { uploadKind = "watermark"; picker.launch("image/*") },
                            enabled = !lhBusy,
                            modifier = Modifier.fillMaxWidth()
                        ) {
                            Icon(Icons.Default.Image, null, Modifier.size(18.dp))
                            Spacer(Modifier.width(6.dp))
                            Text(if (hasWatermark) "Replace Background" else "Upload Background")
                        }
                        Spacer(Modifier.height(10.dp))
                        HorizontalDivider()
                        Spacer(Modifier.height(10.dp))
                        Row(
                            verticalAlignment = Alignment.CenterVertically,
                            horizontalArrangement = Arrangement.spacedBy(8.dp)
                        ) {
                            Icon(
                                if (stampUrl != null) Icons.Default.CheckCircle else Icons.Default.Approval,
                                null,
                                tint = if (stampUrl != null) Color(0xFF388E3C)
                                       else MaterialTheme.colorScheme.primary,
                                modifier = Modifier.size(18.dp)
                            )
                            Text(
                                if (stampUrl != null) "STAMP & SIGN UPLOADED" else "STAMP & SIGN NOT UPLOADED",
                                style = MaterialTheme.typography.labelLarge,
                                fontWeight = FontWeight.Bold,
                                color = if (stampUrl != null) Color(0xFF388E3C)
                                        else MaterialTheme.colorScheme.onSurfaceVariant
                            )
                        }
                        Spacer(Modifier.height(6.dp))
                        Text(
                            "One stamp and signature image, printed on every authorization letter. " +
                            "Drag it on the page below to set where it lands. A transparent PNG works best.",
                            style = MaterialTheme.typography.bodySmall,
                            color = MaterialTheme.colorScheme.onSurfaceVariant
                        )
                        Spacer(Modifier.height(10.dp))
                        OutlinedButton(
                            onClick = { uploadKind = "stamp"; picker.launch("image/*") },
                            enabled = !lhBusy,
                            modifier = Modifier.fillMaxWidth()
                        ) {
                            Icon(Icons.Default.Approval, null, Modifier.size(18.dp))
                            Spacer(Modifier.width(6.dp))
                            Text(if (stampUrl != null) "Replace Stamp & Sign" else "Upload Stamp & Sign")
                        }

                        if (stampUrl != null) {
                            Spacer(Modifier.height(12.dp))
                            StampPlacer(
                                stampUrl = stampUrl!!,
                                x = stampX, y = stampY, w = stampW, h = stampH,
                                onMove = { nx, ny -> stampX = nx; stampY = ny },
                                onSize = { nw, nh -> stampW = nw; stampH = nh }
                            )
                            Spacer(Modifier.height(10.dp))
                            Button(
                                onClick = { saveStampPosition() },
                                enabled = !stampSaving,
                                modifier = Modifier.fillMaxWidth()
                            ) {
                                Icon(Icons.Default.Save, null, Modifier.size(18.dp))
                                Spacer(Modifier.width(6.dp))
                                Text(if (stampSaving) "Saving…" else "Save Stamp Position")
                            }
                        }
                    
                }
            }
        )
    }

    pending?.let { rec ->
        AlertDialog(
            onDismissRequest = { pending = null },
            confirmButton = {
                TextButton(onClick = {
                    val r = rec
                    pending = null
                    generate(r)
                }) { Text("Download") }
            },
            dismissButton = {
                TextButton(onClick = { pending = null }) { Text("Cancel") }
            },
            title = { Text("Letter details", fontWeight = FontWeight.Bold) },
            text = {
                Column {
                    Text(
                        rec.vehicleNo.ifBlank { "\u2014" },
                        fontWeight = FontWeight.Black,
                        fontFamily = RobotoFamily,
                        fontSize = 16.sp
                    )
                    Spacer(Modifier.height(10.dp))
                    OutlinedTextField(
                        value = execName,
                        onValueChange = { execName = it },
                        label = { Text("Authorized executive", fontSize = 11.sp) },
                        singleLine = true,
                        shape = RoundedCornerShape(10.dp),
                        modifier = Modifier.fillMaxWidth()
                    )
                    Spacer(Modifier.height(8.dp))
                    OutlinedTextField(
                        value = execId,
                        onValueChange = { execId = it },
                        label = { Text("Executive ID", fontSize = 11.sp) },
                        singleLine = true,
                        shape = RoundedCornerShape(10.dp),
                        modifier = Modifier.fillMaxWidth()
                    )
                    Spacer(Modifier.height(8.dp))
                    Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                        OutlinedTextField(
                            value = validFrom,
                            onValueChange = { validFrom = it },
                            label = { Text("Valid from", fontSize = 11.sp) },
                            singleLine = true,
                            shape = RoundedCornerShape(10.dp),
                            modifier = Modifier.weight(1f)
                        )
                        OutlinedTextField(
                            value = validTo,
                            onValueChange = { validTo = it },
                            label = { Text("Valid to", fontSize = 11.sp) },
                            singleLine = true,
                            shape = RoundedCornerShape(10.dp),
                            modifier = Modifier.weight(1f)
                        )
                    }
                    Spacer(Modifier.height(6.dp))
                    Text(
                        "Leave any field empty to print a blank line.",
                        style = MaterialTheme.typography.labelSmall,
                        color = MaterialTheme.colorScheme.onSurfaceVariant
                    )
                }
            }
        )
    }
}


@Composable
private fun StampPlacer(
    stampUrl: String,
    x: Float, y: Float, w: Float, h: Float,
    onMove: (Float, Float) -> Unit,
    onSize: (Float, Float) -> Unit
) {
    val pageW = AuthorityLetterPdf.PAGE_W.toFloat()
    val pageH = AuthorityLetterPdf.PAGE_H.toFloat()
    val previewW = 260.dp
    val previewH = previewW * (pageH / pageW)
    val density = LocalDensity.current
    val previewWpx = with(density) { previewW.toPx() }
    val scale = previewWpx / pageW

    Column {
        Text("Position on the page",
            style = MaterialTheme.typography.labelSmall,
            fontWeight = FontWeight.Bold,
            color = MaterialTheme.colorScheme.primary)
        Spacer(Modifier.height(6.dp))
        Box(
            Modifier
                .width(previewW)
                .height(previewH)
                .background(Color.White)
                .border(1.dp, MaterialTheme.colorScheme.outlineVariant)
        ) {
            Box(
                Modifier
                    .offset(
                        x = with(density) { (x * scale).toDp() },
                        y = with(density) { (y * scale).toDp() }
                    )
                    .size(
                        width  = with(density) { (w * scale).toDp() },
                        height = with(density) { (h * scale).toDp() }
                    )
                    .border(1.dp, MaterialTheme.colorScheme.primary)
                    .pointerInput(w, h) {
                        detectDragGestures { change, drag: Offset ->
                            change.consume()
                            onMove(
                                (x + drag.x / scale).coerceIn(0f, pageW - w),
                                (y + drag.y / scale).coerceIn(0f, pageH - h)
                            )
                        }
                    }
            ) {
                AsyncImage(
                    model = stampUrl,
                    contentDescription = "Stamp and signature",
                    contentScale = ContentScale.Fit,
                    modifier = Modifier.fillMaxSize()
                )
            }
        }
        Spacer(Modifier.height(8.dp))
        Text("Size — ${w.toInt()} x ${h.toInt()} pt",
            style = MaterialTheme.typography.labelSmall,
            color = MaterialTheme.colorScheme.onSurfaceVariant)
        Slider(
            value = w,
            onValueChange = { nw ->
                val ratio = if (w > 0f) h / w else 0.55f
                val clamped = nw.coerceIn(40f, pageW - x)
                onSize(clamped, (clamped * ratio).coerceIn(20f, pageH - y))
            },
            valueRange = 40f..300f
        )
        Text("Drag the stamp to move it. Coordinates: ${x.toInt()}, ${y.toInt()} pt from the top-left.",
            style = MaterialTheme.typography.labelSmall,
            color = MaterialTheme.colorScheme.outline)
    }
}

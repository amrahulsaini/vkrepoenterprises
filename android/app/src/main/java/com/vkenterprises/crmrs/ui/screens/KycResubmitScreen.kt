package com.vkenterprises.crmrs.ui.screens

import android.Manifest
import android.content.pm.PackageManager
import android.graphics.BitmapFactory
import android.location.Geocoder
import android.net.Uri
import android.util.Base64
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.IntentSenderRequest
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.*
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.*
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.core.content.ContextCompat
import androidx.navigation.NavController
import coil.compose.AsyncImage
import com.google.android.gms.common.api.ResolvableApiException
import com.google.android.gms.location.LocationRequest
import com.google.android.gms.location.LocationServices
import com.google.android.gms.location.LocationSettingsRequest
import com.google.android.gms.location.Priority
import com.vkenterprises.crmrs.BuildConfig
import com.vkenterprises.crmrs.data.api.ApiClient
import com.vkenterprises.crmrs.data.models.ResubmitKycRequest
import com.vkenterprises.crmrs.navigation.Screen
import com.vkenterprises.crmrs.utils.compressImageToBase64
import com.vkenterprises.crmrs.utils.extractAadhaarNumber
import com.vkenterprises.crmrs.viewmodel.AuthViewModel
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.suspendCancellableCoroutine
import kotlinx.coroutines.withContext
import java.util.Locale
import kotlin.coroutines.resume

private val OK = Color(0xFF16A34A)

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun KycResubmitScreen(vm: AuthViewModel, nav: NavController) {
    val context = LocalContext.current
    val scope = rememberCoroutineScope()
    val scroll = rememberScrollState()

    val mobile = vm.lastMobile
    val slug = BuildConfig.AGENCY_SLUG

    var aadhaarFrontUri by remember { mutableStateOf<Uri?>(null) }
    var aadhaarFrontB64 by remember { mutableStateOf<String?>(null) }
    var aadhaarBackUri by remember { mutableStateOf<Uri?>(null) }
    var aadhaarBackB64 by remember { mutableStateOf<String?>(null) }
    var panFrontUri by remember { mutableStateOf<Uri?>(null) }
    var panFrontB64 by remember { mutableStateOf<String?>(null) }
    var selfieUri by remember { mutableStateOf<Uri?>(null) }
    var selfieB64 by remember { mutableStateOf<String?>(null) }

    var aadhaarNumber by remember { mutableStateOf("") }
    var ocrRunning by remember { mutableStateOf(false) }
    var otpRefId by remember { mutableStateOf<String?>(null) }
    var otp by remember { mutableStateOf("") }
    var sending by remember { mutableStateOf(false) }
    var verifying by remember { mutableStateOf(false) }
    var msg by remember { mutableStateOf("") }
    var verified by remember { mutableStateOf(false) }
    var aaName by remember { mutableStateOf<String?>(null) }
    var aaDob by remember { mutableStateOf<String?>(null) }
    var aaGender by remember { mutableStateOf<String?>(null) }
    var aaAddress by remember { mutableStateOf<String?>(null) }
    var aaPhoto by remember { mutableStateOf<String?>(null) }

    var regLat by remember { mutableStateOf<Double?>(null) }
    var regLng by remember { mutableStateOf<Double?>(null) }
    var regLabel by remember { mutableStateOf<String?>(null) }
    var locating by remember { mutableStateOf(false) }

    var error by remember { mutableStateOf("") }
    var submitting by remember { mutableStateOf(false) }
    var done by remember { mutableStateOf(false) }

    fun b64(uri: Uri): String? = runCatching { compressImageToBase64(context, uri) }.getOrNull()

    fun captureLocation() {
        scope.launch {
            val fine = ContextCompat.checkSelfPermission(context, Manifest.permission.ACCESS_FINE_LOCATION) == PackageManager.PERMISSION_GRANTED
            val coarse = ContextCompat.checkSelfPermission(context, Manifest.permission.ACCESS_COARSE_LOCATION) == PackageManager.PERMISSION_GRANTED
            if (!fine && !coarse) return@launch
            locating = true
            @android.annotation.SuppressLint("MissingPermission")
            val loc = runCatching {
                val c = LocationServices.getFusedLocationProviderClient(context)
                val fresh = suspendCancellableCoroutine<android.location.Location?> { cont ->
                    c.getCurrentLocation(Priority.PRIORITY_HIGH_ACCURACY, null)
                        .addOnSuccessListener { cont.resume(it) }.addOnFailureListener { cont.resume(null) }.addOnCanceledListener { cont.resume(null) }
                }
                fresh ?: suspendCancellableCoroutine<android.location.Location?> { cont ->
                    c.lastLocation.addOnSuccessListener { cont.resume(it) }.addOnFailureListener { cont.resume(null) }.addOnCanceledListener { cont.resume(null) }
                }
            }.getOrNull()
            if (loc != null) {
                regLat = loc.latitude; regLng = loc.longitude
                regLabel = withContext(Dispatchers.IO) {
                    runCatching {
                        @Suppress("DEPRECATION")
                        Geocoder(context, Locale.getDefault()).getFromLocation(loc.latitude, loc.longitude, 1)
                            ?.firstOrNull()?.let { a ->
                                listOfNotNull(a.subLocality, a.locality, a.adminArea, a.postalCode).distinct().joinToString(", ").ifBlank { null }
                            }
                    }.getOrNull()
                }
            }
            locating = false
        }
    }

    val settingsLauncher = rememberLauncherForActivityResult(ActivityResultContracts.StartIntentSenderForResult()) { _ -> captureLocation() }
    fun ensureLocationThenCapture() {
        val req = LocationSettingsRequest.Builder()
            .addLocationRequest(LocationRequest.Builder(Priority.PRIORITY_HIGH_ACCURACY, 1000L).build())
            .setAlwaysShow(true).build()
        LocationServices.getSettingsClient(context).checkLocationSettings(req)
            .addOnSuccessListener { captureLocation() }
            .addOnFailureListener { e ->
                if (e is ResolvableApiException)
                    runCatching { settingsLauncher.launch(IntentSenderRequest.Builder(e.resolution).build()) }.onFailure { captureLocation() }
                else captureLocation()
            }
    }
    val permLauncher = rememberLauncherForActivityResult(ActivityResultContracts.RequestMultiplePermissions()) { g ->
        if (g.values.any { it }) ensureLocationThenCapture()
    }
    fun requestLocation() {
        val fine = ContextCompat.checkSelfPermission(context, Manifest.permission.ACCESS_FINE_LOCATION) == PackageManager.PERMISSION_GRANTED
        val coarse = ContextCompat.checkSelfPermission(context, Manifest.permission.ACCESS_COARSE_LOCATION) == PackageManager.PERMISSION_GRANTED
        if (fine || coarse) ensureLocationThenCapture()
        else permLauncher.launch(arrayOf(Manifest.permission.ACCESS_FINE_LOCATION, Manifest.permission.ACCESS_COARSE_LOCATION))
    }
    LaunchedEffect(Unit) { requestLocation() }

    val aadhaarFrontPicker = rememberLauncherForActivityResult(ActivityResultContracts.GetContent()) { uri ->
        aadhaarFrontUri = uri
        if (uri != null) scope.launch {
            aadhaarFrontB64 = b64(uri); ocrRunning = true
            val num = runCatching { extractAadhaarNumber(context, uri) }.getOrNull()
            if (!num.isNullOrBlank()) { aadhaarNumber = num; verified = false; otpRefId = null; otp = "" }
            ocrRunning = false
        }
    }
    val aadhaarBackPicker = rememberLauncherForActivityResult(ActivityResultContracts.GetContent()) { uri -> aadhaarBackUri = uri; aadhaarBackB64 = uri?.let { b64(it) } }
    val panFrontPicker = rememberLauncherForActivityResult(ActivityResultContracts.GetContent()) { uri -> panFrontUri = uri; panFrontB64 = uri?.let { b64(it) } }
    val selfiePicker = rememberLauncherForActivityResult(ActivityResultContracts.GetContent()) { uri -> selfieUri = uri; selfieB64 = uri?.let { b64(it) } }

    if (done) {
        AlertDialog(
            onDismissRequest = { },
            confirmButton = {
                TextButton(onClick = { nav.navigate(Screen.Login.route) { popUpTo(0) { inclusive = true } } }) { Text("OK") }
            },
            title = { Text("KYC re-submitted") },
            text = { Text("Your documents were re-submitted. Please wait for the agency to verify them, then log in again.") }
        )
    }

    Scaffold(topBar = {
        TopAppBar(title = { Text("Re-submit KYC") }, navigationIcon = {
            IconButton(onClick = { nav.popBackStack() }) { Icon(Icons.Default.ArrowBack, null) }
        })
    }) { pad ->
        Column(
            Modifier.padding(pad).fillMaxSize().imePadding().verticalScroll(scroll).padding(horizontal = 24.dp, vertical = 16.dp),
            verticalArrangement = Arrangement.spacedBy(14.dp)
        ) {
            Text("Re-upload your documents and verify your Aadhaar again. The agency will review and approve your account.",
                style = MaterialTheme.typography.bodyMedium, color = MaterialTheme.colorScheme.onSurfaceVariant)

            RHeader("Aadhaar Verification")
            Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                RImageCard("Aadhaar Front", aadhaarFrontUri, Modifier.weight(1f)) { aadhaarFrontPicker.launch("image/*") }
                RImageCard("Aadhaar Back", aadhaarBackUri, Modifier.weight(1f)) { aadhaarBackPicker.launch("image/*") }
            }
            OutlinedTextField(
                value = aadhaarNumber,
                onValueChange = { v -> aadhaarNumber = v.filter { it.isDigit() }.take(12); verified = false; otpRefId = null },
                label = { Text("Aadhaar Number *") }, leadingIcon = { Icon(Icons.Default.Badge, null) },
                trailingIcon = { if (ocrRunning) RSpinner() else if (verified) Icon(Icons.Default.CheckCircle, null, tint = OK) },
                enabled = !verified, singleLine = true,
                keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number), modifier = Modifier.fillMaxWidth()
            )
            if (!verified) {
                if (otpRefId == null) {
                    Button(onClick = {
                        msg = ""; scope.launch {
                            sending = true
                            val r = runCatching { ApiClient.api.kycAadhaarOtp(mapOf("aadhaarNumber" to aadhaarNumber)) }.getOrNull()
                            val b = r?.body()
                            if (r?.isSuccessful == true && b?.ok == true && b.referenceId != null) { otpRefId = b.referenceId; msg = "OTP sent." }
                            else msg = b?.message ?: "Could not send OTP."
                            sending = false
                        }
                    }, enabled = aadhaarNumber.length == 12 && !sending, modifier = Modifier.fillMaxWidth()) {
                        if (sending) RSpinner(true) else Text("SEND OTP")
                    }
                } else {
                    OutlinedTextField(value = otp, onValueChange = { otp = it.filter { c -> c.isDigit() }.take(6) },
                        label = { Text("Enter OTP *") }, leadingIcon = { Icon(Icons.Default.Sms, null) },
                        keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number), singleLine = true, modifier = Modifier.fillMaxWidth())
                    Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                        OutlinedButton(onClick = { otpRefId = null; otp = ""; msg = "" }, modifier = Modifier.weight(1f)) { Text("Resend") }
                        Button(onClick = {
                            msg = ""; scope.launch {
                                verifying = true
                                val r = runCatching {
                                    ApiClient.api.kycAadhaarVerifyAnon(mapOf("referenceId" to otpRefId, "otp" to otp, "aadhaarNumber" to aadhaarNumber))
                                }.getOrNull()
                                val b = r?.body()
                                if (r?.isSuccessful == true && b?.ok == true && b.verified) {
                                    verified = true; aaName = b.name; aaDob = b.dob; aaGender = b.gender; aaAddress = b.address
                                    aaPhoto = b.photo?.takeIf { it.isNotBlank() }; msg = ""
                                } else msg = b?.message ?: "OTP verification failed."
                                verifying = false
                            }
                        }, enabled = otp.length >= 4 && !verifying, modifier = Modifier.weight(1f)) {
                            if (verifying) RSpinner(true) else Text("VERIFY OTP")
                        }
                    }
                }
            }
            if (msg.isNotEmpty()) Text(msg, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.primary)

            if (verified) {
                Surface(shape = RoundedCornerShape(12.dp), color = OK.copy(alpha = 0.08f),
                    border = BorderStroke(1.dp, OK.copy(alpha = 0.4f)), modifier = Modifier.fillMaxWidth()) {
                    Column(Modifier.padding(14.dp), verticalArrangement = Arrangement.spacedBy(4.dp)) {
                        Row(verticalAlignment = Alignment.CenterVertically) {
                            Icon(Icons.Default.Verified, null, tint = OK, modifier = Modifier.size(18.dp)); Spacer(Modifier.width(6.dp))
                            Text("Aadhaar Verified", fontWeight = FontWeight.Bold, color = OK)
                        }
                        val bmp = remember(aaPhoto) {
                            aaPhoto?.let { runCatching { val by = Base64.decode(it, Base64.DEFAULT); BitmapFactory.decodeByteArray(by, 0, by.size)?.asImageBitmap() }.getOrNull() }
                        }
                        if (bmp != null) Image(bmp, "UIDAI photo", contentScale = ContentScale.Crop,
                            modifier = Modifier.size(72.dp).clip(RoundedCornerShape(8.dp)).border(1.dp, OK.copy(alpha = 0.4f), RoundedCornerShape(8.dp)))
                        RDetail("Name", aaName); RDetail("DOB", aaDob); RDetail("Gender", aaGender); RDetail("Address", aaAddress)
                    }
                }
            }

            RHeader("PAN Card")
            RImageCard("PAN Card Front", panFrontUri, Modifier.fillMaxWidth().height(120.dp)) { panFrontPicker.launch("image/*") }

            RHeader("Selfie with Aadhaar")
            RImageCard("Selfie holding Aadhaar", selfieUri, Modifier.fillMaxWidth().height(160.dp)) { selfiePicker.launch("image/*") }

            RHeader("Current Location")
            Surface(shape = RoundedCornerShape(12.dp), color = MaterialTheme.colorScheme.surfaceVariant.copy(alpha = 0.4f), modifier = Modifier.fillMaxWidth()) {
                Row(Modifier.padding(14.dp).fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
                    Icon(if (regLat != null) Icons.Default.LocationOn else Icons.Default.LocationSearching, null,
                        tint = if (regLat != null) OK else MaterialTheme.colorScheme.primary)
                    Spacer(Modifier.width(10.dp))
                    Column(Modifier.weight(1f)) {
                        when {
                            locating -> Text("Getting your location…")
                            regLat != null -> { Text(regLabel ?: "Location captured", fontWeight = FontWeight.Medium)
                                Text("%.5f, %.5f".format(regLat, regLng), style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant) }
                            else -> Text("Location not captured yet")
                        }
                    }
                    if (locating) RSpinner() else TextButton(onClick = { requestLocation() }) { Text(if (regLat != null) "Refresh" else "Capture") }
                }
            }

            if (error.isNotEmpty()) Card(colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.errorContainer)) {
                Text(error, Modifier.padding(12.dp), color = MaterialTheme.colorScheme.onErrorContainer)
            }

            Button(onClick = {
                error = when {
                    aadhaarFrontB64 == null -> "Aadhaar front photo is required."
                    aadhaarBackB64 == null -> "Aadhaar back photo is required."
                    aadhaarNumber.length != 12 -> "Enter your 12-digit Aadhaar number."
                    !verified -> "Please verify your Aadhaar with the OTP."
                    panFrontB64 == null -> "PAN card photo is required."
                    selfieB64 == null -> "Selfie holding your Aadhaar is required."
                    regLat == null || regLng == null -> "Please capture your current location."
                    mobile.isBlank() -> "Could not determine your mobile. Go back and log in again."
                    else -> ""
                }
                if (error.isNotEmpty()) return@Button
                scope.launch {
                    submitting = true
                    val r = runCatching {
                        ApiClient.api.kycResubmit(ResubmitKycRequest(
                            slug = slug, mobile = mobile,
                            aadhaarFront = aadhaarFrontB64, aadhaarBack = aadhaarBackB64, panFront = panFrontB64,
                            selfieWithAadhaar = selfieB64, aadhaarPhoto = aaPhoto,
                            aadhaarNumber = aadhaarNumber, aadhaarName = aaName, aadhaarDob = aaDob,
                            aadhaarGender = aaGender, aadhaarAddress = aaAddress, aadhaarVerified = verified,
                            regLat = regLat, regLng = regLng, regLocation = regLabel
                        ))
                    }.getOrNull()
                    submitting = false
                    if (r?.isSuccessful == true) done = true
                    else error = "Re-submit failed. Please check your connection and try again."
                }
            }, enabled = !submitting, modifier = Modifier.fillMaxWidth().height(52.dp)) {
                if (submitting) RSpinner(true) else Text("RE-SUBMIT KYC", fontWeight = FontWeight.Bold, fontSize = 16.sp)
            }
            Spacer(Modifier.height(16.dp))
        }
    }
}

@Composable
private fun RHeader(text: String) {
    Text(text, style = MaterialTheme.typography.titleSmall, fontWeight = FontWeight.Bold,
        color = MaterialTheme.colorScheme.primary, modifier = Modifier.padding(top = 6.dp))
    HorizontalDivider(color = MaterialTheme.colorScheme.primary.copy(alpha = 0.3f))
}

@Composable
private fun RDetail(label: String, value: String?) {
    if (value.isNullOrBlank()) return
    Row { Text("$label: ", fontWeight = FontWeight.SemiBold, style = MaterialTheme.typography.bodySmall)
        Text(value, style = MaterialTheme.typography.bodySmall) }
}

@Composable
private fun RSpinner(onPrimary: Boolean = false) {
    CircularProgressIndicator(Modifier.size(18.dp), strokeWidth = 2.dp,
        color = if (onPrimary) MaterialTheme.colorScheme.onPrimary else MaterialTheme.colorScheme.primary)
}

@Composable
private fun RImageCard(label: String, uri: Uri?, modifier: Modifier = Modifier, onClick: () -> Unit) {
    OutlinedCard(onClick = onClick, modifier = modifier.defaultMinSize(minHeight = 110.dp)) {
        Box(Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
            if (uri != null) {
                AsyncImage(model = uri, contentDescription = label, contentScale = ContentScale.Crop, modifier = Modifier.fillMaxSize())
                Box(Modifier.fillMaxWidth().align(Alignment.BottomCenter)
                    .background(MaterialTheme.colorScheme.surface.copy(alpha = 0.75f)).padding(4.dp), contentAlignment = Alignment.Center) {
                    Text(label, style = MaterialTheme.typography.labelSmall)
                }
            } else {
                Column(horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.spacedBy(4.dp), modifier = Modifier.padding(12.dp)) {
                    Icon(Icons.Default.AddAPhoto, null, Modifier.size(28.dp), tint = MaterialTheme.colorScheme.primary)
                    Text(label, style = MaterialTheme.typography.labelSmall, color = MaterialTheme.colorScheme.onSurfaceVariant, textAlign = TextAlign.Center)
                }
            }
        }
    }
}

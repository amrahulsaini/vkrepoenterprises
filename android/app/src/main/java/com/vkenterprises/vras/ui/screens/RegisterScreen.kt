package com.vkenterprises.vras.ui.screens

import android.Manifest
import android.content.pm.PackageManager
import android.graphics.BitmapFactory
import android.location.Geocoder
import android.net.Uri
import android.util.Base64
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.IntentSenderRequest
import androidx.activity.result.contract.ActivityResultContracts
import androidx.core.content.ContextCompat
import androidx.compose.ui.graphics.asImageBitmap
import com.vkenterprises.vras.utils.compressImageToBase64
import com.vkenterprises.vras.utils.extractAadhaarNumber
import androidx.compose.foundation.*
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.relocation.BringIntoViewRequester
import androidx.compose.foundation.relocation.bringIntoViewRequester
import androidx.compose.foundation.shape.*
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.*
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.*
import androidx.compose.ui.draw.clip
import androidx.compose.ui.focus.onFocusEvent
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.LocalFocusManager
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.unit.*
import androidx.navigation.NavController
import androidx.compose.foundation.Image
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.res.painterResource
import coil.compose.AsyncImage
import com.google.android.gms.common.api.ResolvableApiException
import com.google.android.gms.location.LocationRequest
import com.google.android.gms.location.LocationServices
import com.google.android.gms.location.LocationSettingsRequest
import com.google.android.gms.location.Priority
import com.vkenterprises.vras.BuildConfig
import com.vkenterprises.vras.R
import com.vkenterprises.vras.data.api.ApiClient
import com.vkenterprises.vras.navigation.Screen
import com.vkenterprises.vras.viewmodel.AuthUiState
import com.vkenterprises.vras.viewmodel.AuthViewModel
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.suspendCancellableCoroutine
import kotlinx.coroutines.withContext
import java.util.Locale
import kotlin.coroutines.resume

private val OK_GREEN = Color(0xFF16A34A)
private val ERR_RED  = Color(0xFFDC2626)

@OptIn(ExperimentalMaterial3Api::class, ExperimentalFoundationApi::class)
@Composable
fun RegisterScreen(vm: AuthViewModel, nav: NavController) {
    val context      = LocalContext.current
    val state        by vm.state.collectAsState()
    val focusManager = LocalFocusManager.current
    val scope        = rememberCoroutineScope()
    val scrollState  = rememberScrollState()

    var mobile   by remember { mutableStateOf("") }
    var name     by remember { mutableStateOf("") }
    var address  by remember { mutableStateOf("") }
    var pincode  by remember { mutableStateOf("") }
    var pfpUri   by remember { mutableStateOf<Uri?>(null) }
    var pfpB64   by remember { mutableStateOf<String?>(null) }

    var aadhaarFrontUri by remember { mutableStateOf<Uri?>(null) }
    var aadhaarFrontB64 by remember { mutableStateOf<String?>(null) }
    var aadhaarBackUri  by remember { mutableStateOf<Uri?>(null) }
    var aadhaarBackB64  by remember { mutableStateOf<String?>(null) }
    var panFrontUri     by remember { mutableStateOf<Uri?>(null) }
    var panFrontB64     by remember { mutableStateOf<String?>(null) }

    var aadhaarNumber by remember { mutableStateOf("") }
    var ocrRunning    by remember { mutableStateOf(false) }
    var otpRefId      by remember { mutableStateOf<String?>(null) }
    var otp           by remember { mutableStateOf("") }
    var otpSending    by remember { mutableStateOf(false) }
    var otpVerifying  by remember { mutableStateOf(false) }
    var kycMsg        by remember { mutableStateOf("") }
    var aadhaarVerified by remember { mutableStateOf(false) }
    var aaName    by remember { mutableStateOf<String?>(null) }
    var aaDob     by remember { mutableStateOf<String?>(null) }
    var aaGender  by remember { mutableStateOf<String?>(null) }
    var aaAddress by remember { mutableStateOf<String?>(null) }
    var aaPhoto   by remember { mutableStateOf<String?>(null) }

    var regLat   by remember { mutableStateOf<Double?>(null) }
    var regLng   by remember { mutableStateOf<Double?>(null) }
    var regLabel by remember { mutableStateOf<String?>(null) }
    var locating by remember { mutableStateOf(false) }

    var error by remember { mutableStateOf("") }

    var mobileOtpSent  by remember { mutableStateOf(false) }
    var mobileOtp      by remember { mutableStateOf("") }
    var mobileVerified by remember { mutableStateOf(false) }
    var mobileOtpBusy  by remember { mutableStateOf(false) }
    var mobileOtpMsg   by remember { mutableStateOf("") }
    var mobileCooldown by remember { mutableStateOf(0) }
    LaunchedEffect(mobileCooldown) {
        if (mobileCooldown > 0) { kotlinx.coroutines.delay(1000); mobileCooldown-- }
    }

    val agencySlug = BuildConfig.AGENCY_SLUG
    val agencyName = BuildConfig.AGENCY_NAME

    fun uriToBase64(uri: Uri): String? = runCatching { compressImageToBase64(context, uri) }.getOrNull()

    fun captureLocation() {
        scope.launch {
            val fine = ContextCompat.checkSelfPermission(context, Manifest.permission.ACCESS_FINE_LOCATION) ==
                    PackageManager.PERMISSION_GRANTED
            val coarse = ContextCompat.checkSelfPermission(context, Manifest.permission.ACCESS_COARSE_LOCATION) ==
                    PackageManager.PERMISSION_GRANTED
            if (!fine && !coarse) return@launch
            locating = true
            @android.annotation.SuppressLint("MissingPermission")
            val loc = runCatching {
                val client = LocationServices.getFusedLocationProviderClient(context)
                val fresh = suspendCancellableCoroutine<android.location.Location?> { cont ->
                    client.getCurrentLocation(Priority.PRIORITY_HIGH_ACCURACY, null)
                        .addOnSuccessListener { cont.resume(it) }
                        .addOnFailureListener { cont.resume(null) }
                        .addOnCanceledListener { cont.resume(null) }
                }
                fresh ?: suspendCancellableCoroutine<android.location.Location?> { cont ->
                    client.lastLocation
                        .addOnSuccessListener { cont.resume(it) }
                        .addOnFailureListener { cont.resume(null) }
                        .addOnCanceledListener { cont.resume(null) }
                }
            }.getOrNull()
            if (loc != null) {
                regLat = loc.latitude; regLng = loc.longitude
                regLabel = withContext(Dispatchers.IO) {
                    runCatching {
                        @Suppress("DEPRECATION")
                        Geocoder(context, Locale.getDefault())
                            .getFromLocation(loc.latitude, loc.longitude, 1)
                            ?.firstOrNull()
                            ?.let { a ->
                                listOfNotNull(a.subLocality, a.locality, a.adminArea, a.postalCode)
                                    .distinct().joinToString(", ").ifBlank { null }
                            }
                    }.getOrNull()
                }
            }
            locating = false
        }
    }

    val settingsLauncher = rememberLauncherForActivityResult(
        ActivityResultContracts.StartIntentSenderForResult()
    ) { _ -> captureLocation() }

    fun ensureLocationThenCapture() {
        val request = LocationSettingsRequest.Builder()
            .addLocationRequest(
                LocationRequest.Builder(Priority.PRIORITY_HIGH_ACCURACY, 1000L).build()
            )
            .setAlwaysShow(true)
            .build()
        LocationServices.getSettingsClient(context).checkLocationSettings(request)
            .addOnSuccessListener { captureLocation() }
            .addOnFailureListener { e ->
                if (e is ResolvableApiException) {
                    runCatching {
                        settingsLauncher.launch(IntentSenderRequest.Builder(e.resolution).build())
                    }.onFailure { captureLocation() }
                } else captureLocation()
            }
    }

    val locationPermLauncher = rememberLauncherForActivityResult(
        ActivityResultContracts.RequestMultiplePermissions()
    ) { granted ->
        if (granted.values.any { it }) ensureLocationThenCapture()
    }

    fun requestLocation() {
        val fine = ContextCompat.checkSelfPermission(context, Manifest.permission.ACCESS_FINE_LOCATION) ==
                PackageManager.PERMISSION_GRANTED
        val coarse = ContextCompat.checkSelfPermission(context, Manifest.permission.ACCESS_COARSE_LOCATION) ==
                PackageManager.PERMISSION_GRANTED
        if (fine || coarse) ensureLocationThenCapture()
        else locationPermLauncher.launch(
            arrayOf(Manifest.permission.ACCESS_FINE_LOCATION, Manifest.permission.ACCESS_COARSE_LOCATION)
        )
    }

    val pfpPicker = rememberLauncherForActivityResult(ActivityResultContracts.GetContent()) { uri ->
        pfpUri = uri; pfpB64 = uri?.let { uriToBase64(it) }
    }
    val aadhaarFrontPicker = rememberLauncherForActivityResult(ActivityResultContracts.GetContent()) { uri ->
        aadhaarFrontUri = uri
        if (uri != null) scope.launch {
            aadhaarFrontB64 = uriToBase64(uri)
            ocrRunning = true
            val num = runCatching { extractAadhaarNumber(context, uri) }.getOrNull()
            if (!num.isNullOrBlank()) {
                aadhaarNumber = num
                aadhaarVerified = false; otpRefId = null; otp = ""
            }
            ocrRunning = false
        }
    }
    val aadhaarBackPicker = rememberLauncherForActivityResult(ActivityResultContracts.GetContent()) { uri ->
        aadhaarBackUri = uri; aadhaarBackB64 = uri?.let { uriToBase64(it) }
    }
    val panFrontPicker = rememberLauncherForActivityResult(ActivityResultContracts.GetContent()) { uri ->
        panFrontUri = uri; panFrontB64 = uri?.let { uriToBase64(it) }
    }

    LaunchedEffect(Unit) { requestLocation() }

    LaunchedEffect(state) {
        when (state) {
            is AuthUiState.RegisterSuccess -> {
                vm.resetState()
                nav.navigate(Screen.WaitingApproval.go("registered")) {
                    popUpTo(Screen.Register.route) { inclusive = true }
                }
            }
            is AuthUiState.Error -> { error = (state as AuthUiState.Error).message; vm.resetState() }
            else -> {}
        }
    }

    if (state is AuthUiState.Loading) {
        UploadingDialog(
            hasImages = listOf(pfpB64, aadhaarFrontB64, aadhaarBackB64, panFrontB64).any { it != null },
        )
    }

    Scaffold(
        topBar = {
            TopAppBar(
                title = { Text("Create Account") },
                navigationIcon = {
                    IconButton(onClick = { nav.popBackStack() }) {
                        Icon(Icons.Default.ArrowBack, null)
                    }
                }
            )
        }
    ) { pad ->
        Column(
            Modifier
                .padding(pad)
                .fillMaxSize()
                .imePadding()
                .verticalScroll(scrollState)
                .padding(horizontal = 24.dp, vertical = 16.dp),
            verticalArrangement = Arrangement.spacedBy(14.dp)
        ) {

            Box(Modifier.align(Alignment.CenterHorizontally), contentAlignment = Alignment.BottomEnd) {
                if (pfpUri != null) {
                    AsyncImage(
                        model = pfpUri, contentDescription = null,
                        contentScale = ContentScale.Crop,
                        modifier = Modifier.size(88.dp).clip(CircleShape)
                            .border(2.dp, MaterialTheme.colorScheme.primary, CircleShape)
                            .clickable { pfpPicker.launch("image/*") }
                    )
                } else {
                    Box(
                        Modifier.size(88.dp).clip(CircleShape)
                            .background(MaterialTheme.colorScheme.primaryContainer)
                            .clickable { pfpPicker.launch("image/*") },
                        contentAlignment = Alignment.Center
                    ) {
                        Icon(Icons.Default.Person, null, Modifier.size(44.dp),
                            tint = MaterialTheme.colorScheme.primary)
                    }
                }
                SmallFloatingActionButton(
                    onClick = { pfpPicker.launch("image/*") },
                    containerColor = MaterialTheme.colorScheme.primary,
                    contentColor   = MaterialTheme.colorScheme.onPrimary,
                    modifier = Modifier.size(26.dp)
                ) { Icon(Icons.Default.CameraAlt, null, Modifier.size(14.dp)) }
            }
            Text(
                "Profile photo (optional)",
                Modifier.align(Alignment.CenterHorizontally),
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant
            )

            Surface(
                shape = RoundedCornerShape(14.dp),
                color = Color.White,
                shadowElevation = 1.dp,
                modifier = Modifier.fillMaxWidth().align(Alignment.CenterHorizontally)
            ) {
                Column(
                    Modifier.padding(vertical = 16.dp, horizontal = 12.dp),
                    horizontalAlignment = Alignment.CenterHorizontally
                ) {
                    Image(
                        painter = painterResource(id = R.drawable.agency_logo),
                        contentDescription = agencyName,
                        contentScale = ContentScale.Fit,
                        modifier = Modifier.size(72.dp).clip(RoundedCornerShape(12.dp))
                    )
                    Spacer(Modifier.height(8.dp))
                    Text(agencyName,
                        style = MaterialTheme.typography.titleMedium,
                        fontWeight = FontWeight.Bold,
                        color = MaterialTheme.colorScheme.onSurface)
                }
            }

            SectionHeader("Basic Information")

            FocusedField(scrollState) {
                OutlinedTextField(
                    value = mobile,
                    onValueChange = {
                        if (!mobileVerified) { mobile = it; mobileOtpSent = false; mobileOtp = "" }
                    },
                    label = { Text("Mobile Number *") },
                    leadingIcon = { Icon(Icons.Default.Phone, null) },
                    trailingIcon = { if (mobileVerified) Icon(Icons.Default.CheckCircle, null, tint = OK_GREEN) },
                    enabled = !mobileVerified,
                    keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Phone, imeAction = ImeAction.Next),
                    singleLine = true, modifier = Modifier.fillMaxWidth().then(it)
                )
            }

            if (!mobileVerified) {
                if (!mobileOtpSent) {
                    Button(
                        onClick = {
                            focusManager.clearFocus(); mobileOtpMsg = ""
                            if (mobile.isBlank()) { mobileOtpMsg = "Enter your mobile number first."; return@Button }
                            mobileOtpBusy = true
                            vm.checkMobile(mobile, agencySlug) { registered, checkErr ->
                                when {
                                    checkErr != null -> { mobileOtpBusy = false; mobileOtpMsg = checkErr }
                                    registered == true -> {
                                        mobileOtpBusy = false
                                        mobileOtpMsg = "This mobile number is already registered. Please log in instead."
                                    }
                                    else -> vm.sendOtp(mobile) { ok, msg ->
                                        mobileOtpBusy = false
                                        if (ok) { mobileOtpSent = true; mobileCooldown = 30; mobileOtpMsg = "OTP sent to $mobile." }
                                        else mobileOtpMsg = msg
                                    }
                                }
                            }
                        },
                        enabled = !mobileOtpBusy,
                        modifier = Modifier.fillMaxWidth()
                    ) { if (mobileOtpBusy) Spinner(onPrimary = true) else Text("VERIFY MOBILE — SEND OTP") }
                } else {
                    OutlinedTextField(
                        value = mobileOtp,
                        onValueChange = { mobileOtp = it.filter { c -> c.isDigit() }.take(6) },
                        label = { Text("Enter mobile OTP *") },
                        leadingIcon = { Icon(Icons.Default.Sms, null) },
                        keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
                        singleLine = true, modifier = Modifier.fillMaxWidth()
                    )
                    Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                        OutlinedButton(
                            onClick = {
                                mobileOtpMsg = ""; mobileOtpBusy = true
                                vm.sendOtp(mobile) { ok, msg ->
                                    mobileOtpBusy = false
                                    if (ok) { mobileCooldown = 30; mobileOtpMsg = "OTP resent." } else mobileOtpMsg = msg
                                }
                            },
                            enabled = !mobileOtpBusy && mobileCooldown == 0, modifier = Modifier.weight(1f)
                        ) { Text(if (mobileCooldown > 0) "Resend in ${mobileCooldown}s" else "Resend") }
                        Button(
                            onClick = {
                                focusManager.clearFocus(); mobileOtpMsg = ""; mobileOtpBusy = true
                                vm.verifyOtp(mobile, mobileOtp) { ok, msg ->
                                    mobileOtpBusy = false
                                    if (ok) { mobileVerified = true; mobileOtpMsg = "" } else mobileOtpMsg = msg
                                }
                            },
                            enabled = mobileOtp.length >= 4 && !mobileOtpBusy, modifier = Modifier.weight(1f)
                        ) { if (mobileOtpBusy) Spinner(onPrimary = true) else Text("VERIFY OTP") }
                    }
                }
                if (mobileOtpMsg.isNotEmpty())
                    Text(mobileOtpMsg, style = MaterialTheme.typography.bodySmall,
                        color = MaterialTheme.colorScheme.primary)
            }

            FocusedField(scrollState) {
                OutlinedTextField(
                    value = name, onValueChange = { name = it },
                    label = { Text("Full Name *") },
                    leadingIcon = { Icon(Icons.Default.Person, null) },
                    keyboardOptions = KeyboardOptions(imeAction = ImeAction.Next),
                    singleLine = true, modifier = Modifier.fillMaxWidth().then(it)
                )
            }
            FocusedField(scrollState) {
                OutlinedTextField(
                    value = address, onValueChange = { address = it },
                    label = { Text("Address *") },
                    leadingIcon = { Icon(Icons.Default.Home, null) },
                    keyboardOptions = KeyboardOptions(imeAction = ImeAction.Next),
                    maxLines = 3, modifier = Modifier.fillMaxWidth().then(it)
                )
            }
            FocusedField(scrollState) {
                OutlinedTextField(
                    value = pincode, onValueChange = { pincode = it.take(6) },
                    label = { Text("Pincode *") },
                    leadingIcon = { Icon(Icons.Default.LocationOn, null) },
                    keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number, imeAction = ImeAction.Next),
                    singleLine = true, modifier = Modifier.fillMaxWidth().then(it)
                )
            }

            SectionHeader("Aadhaar Verification")
            Text(
                "Upload your Aadhaar front photo — we read the number automatically — " +
                    "then verify it with the OTP sent to your Aadhaar-linked mobile.",
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant
            )

            Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                KycImageCard(
                    label = "Aadhaar Front",
                    uri = aadhaarFrontUri,
                    modifier = Modifier.weight(1f),
                    onClick = { aadhaarFrontPicker.launch("image/*") }
                )
                KycImageCard(
                    label = "Aadhaar Back",
                    uri = aadhaarBackUri,
                    modifier = Modifier.weight(1f),
                    onClick = { aadhaarBackPicker.launch("image/*") }
                )
            }

            OutlinedTextField(
                value = aadhaarNumber,
                onValueChange = { v -> aadhaarNumber = v.filter { it.isDigit() }.take(12)
                    aadhaarVerified = false; otpRefId = null },
                label = { Text("Aadhaar Number *") },
                leadingIcon = { Icon(Icons.Default.Badge, null) },
                trailingIcon = {
                    when {
                        ocrRunning      -> Spinner()
                        aadhaarVerified -> Icon(Icons.Default.CheckCircle, null, tint = OK_GREEN)
                        else            -> {}
                    }
                },
                supportingText = { if (ocrRunning) Text("Reading number from photo…") },
                enabled = !aadhaarVerified,
                keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
                singleLine = true, modifier = Modifier.fillMaxWidth()
            )

            if (!aadhaarVerified) {
                if (otpRefId == null) {
                    Button(
                        onClick = {
                            focusManager.clearFocus(); kycMsg = ""
                            scope.launch {
                                otpSending = true
                                val r = runCatching {
                                    ApiClient.api.kycAadhaarOtp(mapOf("aadhaarNumber" to aadhaarNumber))
                                }.getOrNull()
                                val body = r?.body()
                                if (r?.isSuccessful == true && body?.ok == true && body.referenceId != null) {
                                    otpRefId = body.referenceId
                                    kycMsg = "OTP sent to your Aadhaar-linked mobile."
                                } else {
                                    kycMsg = body?.message ?: "Could not send OTP. Check the Aadhaar number."
                                }
                                otpSending = false
                            }
                        },
                        enabled = aadhaarNumber.length == 12 && !otpSending,
                        modifier = Modifier.fillMaxWidth()
                    ) { if (otpSending) Spinner(onPrimary = true) else Text("SEND OTP") }
                } else {
                    OutlinedTextField(
                        value = otp, onValueChange = { otp = it.filter { c -> c.isDigit() }.take(6) },
                        label = { Text("Enter OTP *") },
                        leadingIcon = { Icon(Icons.Default.Sms, null) },
                        keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
                        singleLine = true, modifier = Modifier.fillMaxWidth()
                    )
                    Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                        OutlinedButton(
                            onClick = { otpRefId = null; otp = ""; kycMsg = "" },
                            modifier = Modifier.weight(1f)
                        ) { Text("Resend") }
                        Button(
                            onClick = {
                                focusManager.clearFocus(); kycMsg = ""
                                scope.launch {
                                    otpVerifying = true
                                    val r = runCatching {
                                        ApiClient.api.kycAadhaarVerifyAnon(
                                            mapOf("referenceId" to otpRefId, "otp" to otp, "aadhaarNumber" to aadhaarNumber)
                                        )
                                    }.getOrNull()
                                    val body = r?.body()
                                    if (r?.isSuccessful == true && body?.ok == true && body.verified) {
                                        aadhaarVerified = true
                                        aaName = body.name; aaDob = body.dob
                                        aaGender = body.gender; aaAddress = body.address
                                        aaPhoto = body.photo?.takeIf { it.isNotBlank() }
                                        kycMsg = ""
                                    } else {
                                        kycMsg = body?.message ?: "OTP verification failed. Try again."
                                    }
                                    otpVerifying = false
                                }
                            },
                            enabled = otp.length >= 4 && !otpVerifying,
                            modifier = Modifier.weight(1f)
                        ) { if (otpVerifying) Spinner(onPrimary = true) else Text("VERIFY OTP") }
                    }
                }
            }

            if (kycMsg.isNotEmpty()) {
                Text(kycMsg, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.primary)
            }

            if (aadhaarVerified) {
                Surface(
                    shape = RoundedCornerShape(12.dp),
                    color = OK_GREEN.copy(alpha = 0.08f),
                    border = BorderStroke(1.dp, OK_GREEN.copy(alpha = 0.4f)),
                    modifier = Modifier.fillMaxWidth()
                ) {
                    Column(Modifier.padding(14.dp), verticalArrangement = Arrangement.spacedBy(4.dp)) {
                        Row(verticalAlignment = Alignment.CenterVertically) {
                            Icon(Icons.Default.Verified, null, tint = OK_GREEN, modifier = Modifier.size(18.dp))
                            Spacer(Modifier.width(6.dp))
                            Text("Aadhaar Verified", fontWeight = FontWeight.Bold, color = OK_GREEN)
                        }
                        val uidaiBmp = remember(aaPhoto) {
                            aaPhoto?.let {
                                runCatching {
                                    val b = Base64.decode(it, Base64.DEFAULT)
                                    BitmapFactory.decodeByteArray(b, 0, b.size)?.asImageBitmap()
                                }.getOrNull()
                            }
                        }
                        if (uidaiBmp != null) {
                            Row(verticalAlignment = Alignment.CenterVertically) {
                                Image(
                                    bitmap = uidaiBmp, contentDescription = "UIDAI photo",
                                    contentScale = ContentScale.Crop,
                                    modifier = Modifier.size(72.dp).clip(RoundedCornerShape(8.dp))
                                        .border(1.dp, OK_GREEN.copy(alpha = 0.4f), RoundedCornerShape(8.dp))
                                )
                                Spacer(Modifier.width(10.dp))
                                Text("Photo from UIDAI", style = MaterialTheme.typography.bodySmall,
                                    color = MaterialTheme.colorScheme.onSurfaceVariant)
                            }
                        }
                        Detail("Name", aaName)
                        Detail("Date of Birth", aaDob)
                        Detail("Gender", aaGender)
                        Detail("Address", aaAddress)
                    }
                }
            }

            SectionHeader("PAN Card (optional)")
            KycImageCard(
                label = "PAN Card Front",
                uri = panFrontUri,
                modifier = Modifier.fillMaxWidth().height(120.dp),
                onClick = { panFrontPicker.launch("image/*") }
            )

            SectionHeader("Current Location")
            Surface(
                shape = RoundedCornerShape(12.dp),
                color = MaterialTheme.colorScheme.surfaceVariant.copy(alpha = 0.4f),
                modifier = Modifier.fillMaxWidth()
            ) {
                Row(
                    Modifier.padding(14.dp).fillMaxWidth(),
                    verticalAlignment = Alignment.CenterVertically
                ) {
                    Icon(
                        if (regLat != null) Icons.Default.LocationOn else Icons.Default.LocationSearching,
                        null, tint = if (regLat != null) OK_GREEN else MaterialTheme.colorScheme.primary
                    )
                    Spacer(Modifier.width(10.dp))
                    Column(Modifier.weight(1f)) {
                        when {
                            locating       -> Text("Getting your location…")
                            regLat != null -> {
                                Text(regLabel ?: "Location captured", fontWeight = FontWeight.Medium)
                                Text("%.5f, %.5f".format(regLat, regLng),
                                    style = MaterialTheme.typography.bodySmall,
                                    color = MaterialTheme.colorScheme.onSurfaceVariant)
                            }
                            else -> Text("Location not captured yet")
                        }
                    }
                    if (locating) Spinner() else TextButton(onClick = { requestLocation() }) {
                        Text(if (regLat != null) "Refresh" else "Capture")
                    }
                }
            }

            if (error.isNotEmpty()) {
                Card(colors = CardDefaults.cardColors(
                    containerColor = MaterialTheme.colorScheme.errorContainer)) {
                    Text(error, Modifier.padding(12.dp),
                        color = MaterialTheme.colorScheme.onErrorContainer)
                }
            }

            Button(
                onClick = {
                    focusManager.clearFocus()
                    error = when {
                        mobile.isBlank() || name.isBlank() -> "Mobile and name are required."
                        !mobileVerified                    -> "Please verify your mobile number with the OTP."
                        address.isBlank()                  -> "Address is required."
                        pincode.isBlank()                  -> "Pincode is required."
                        aadhaarFrontB64 == null            -> "Aadhaar front photo is required."
                        aadhaarBackB64 == null             -> "Aadhaar back photo is required."
                        aadhaarNumber.length != 12         -> "Enter your 12-digit Aadhaar number."
                        !aadhaarVerified                   -> "Please verify your Aadhaar with the OTP."
                        regLat == null || regLng == null   -> "Please capture your current location."
                        else                               -> ""
                    }
                    if (error.isNotEmpty()) return@Button
                    vm.register(
                        mobile, name,
                        address, pincode,
                        pfpB64,
                        aadhaarFrontB64, aadhaarBackB64, panFrontB64,
                        accountNumber = null, ifscCode = null,
                        slug = agencySlug, agencyName = agencyName, agencyMobile = "",
                        selfieWithAadhaar = null,
                        aadhaarNumber = aadhaarNumber,
                        aadhaarName = aaName, aadhaarDob = aaDob, aadhaarGender = aaGender,
                        aadhaarAddress = aaAddress, aadhaarVerified = aadhaarVerified,
                        regLat = regLat, regLng = regLng, regLocation = regLabel,
                        aadhaarPhoto = aaPhoto
                    )
                },
                enabled  = state !is AuthUiState.Loading,
                modifier = Modifier.fillMaxWidth().height(52.dp)
            ) {
                if (state is AuthUiState.Loading)
                    CircularProgressIndicator(Modifier.size(20.dp), strokeWidth = 2.dp,
                        color = MaterialTheme.colorScheme.onPrimary)
                else
                    Text("REGISTER", fontWeight = FontWeight.Bold, fontSize = 16.sp)
            }

            TextButton(
                onClick = { nav.popBackStack() },
                modifier = Modifier.align(Alignment.CenterHorizontally)
            ) {
                Text("Already have an account? Login")
            }

            Spacer(Modifier.height(16.dp))
        }
    }
}

@OptIn(ExperimentalFoundationApi::class)
@Composable
private fun FocusedField(
    scrollState: ScrollState,
    content: @Composable (Modifier) -> Unit
) {
    val requester = remember { BringIntoViewRequester() }
    val scope     = rememberCoroutineScope()
    content(
        Modifier.bringIntoViewRequester(requester).onFocusEvent { fs ->
            if (fs.isFocused) scope.launch { requester.bringIntoView() }
        }
    )
}

@Composable
private fun SectionHeader(text: String) {
    Text(
        text,
        style = MaterialTheme.typography.titleSmall,
        fontWeight = FontWeight.Bold,
        color = MaterialTheme.colorScheme.primary,
        modifier = Modifier.padding(top = 6.dp)
    )
    HorizontalDivider(color = MaterialTheme.colorScheme.primary.copy(alpha = 0.3f))
}

@Composable
private fun Detail(label: String, value: String?) {
    if (value.isNullOrBlank()) return
    Row {
        Text("$label: ", fontWeight = FontWeight.SemiBold,
            style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurface)
        Text(value, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurface)
    }
}

@Composable
private fun Spinner(onPrimary: Boolean = false) {
    CircularProgressIndicator(
        Modifier.size(18.dp), strokeWidth = 2.dp,
        color = if (onPrimary) MaterialTheme.colorScheme.onPrimary else MaterialTheme.colorScheme.primary
    )
}

@Composable
private fun KycImageCard(
    label: String,
    uri: Uri?,
    modifier: Modifier = Modifier,
    onClick: () -> Unit
) {
    OutlinedCard(
        onClick = onClick,
        modifier = modifier.defaultMinSize(minHeight = 110.dp)
    ) {
        Box(Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
            if (uri != null) {
                AsyncImage(
                    model = uri, contentDescription = label,
                    contentScale = ContentScale.Crop,
                    modifier = Modifier.fillMaxSize()
                )
                Box(
                    Modifier.fillMaxWidth().align(Alignment.BottomCenter)
                        .background(MaterialTheme.colorScheme.surface.copy(alpha = 0.75f))
                        .padding(4.dp),
                    contentAlignment = Alignment.Center
                ) {
                    Text(label, style = MaterialTheme.typography.labelSmall,
                        color = MaterialTheme.colorScheme.onSurface)
                }
            } else {
                Column(
                    horizontalAlignment = Alignment.CenterHorizontally,
                    verticalArrangement = Arrangement.spacedBy(4.dp),
                    modifier = Modifier.padding(12.dp)
                ) {
                    Icon(Icons.Default.AddAPhoto, null,
                        Modifier.size(28.dp),
                        tint = MaterialTheme.colorScheme.primary)
                    Text(label,
                        style = MaterialTheme.typography.labelSmall,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                        textAlign = androidx.compose.ui.text.style.TextAlign.Center)
                }
            }
        }
    }
}

@Composable
private fun UploadingDialog(hasImages: Boolean) {
    var step by remember { mutableStateOf(0) }
    val steps = if (hasImages) listOf(
        "Compressing your photos…",
        "Uploading documents to server…",
        "Almost done — verifying your details…"
    ) else listOf(
        "Sending your details to server…",
        "Almost done — verifying…",
        "Just a moment…"
    )
    LaunchedEffect(Unit) {
        while (true) {
            kotlinx.coroutines.delay(2200)
            step = (step + 1) % steps.size
        }
    }
    AlertDialog(
        onDismissRequest = { },
        confirmButton = { },
        title = {
            Text("Creating your account",
                fontWeight = FontWeight.Bold,
                style = MaterialTheme.typography.titleMedium)
        },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(14.dp)) {
                LinearProgressIndicator(
                    modifier = Modifier.fillMaxWidth(),
                    color = MaterialTheme.colorScheme.primary,
                    trackColor = MaterialTheme.colorScheme.primary.copy(alpha = 0.15f)
                )
                Text(steps[step],
                    style = MaterialTheme.typography.bodyMedium,
                    color = MaterialTheme.colorScheme.onSurface)
                Text("Please keep the app open. This usually takes 5–15 seconds depending on your network.",
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant)
            }
        }
    )
}

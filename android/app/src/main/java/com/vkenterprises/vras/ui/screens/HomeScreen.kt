package com.vkenterprises.vras.ui.screens

import android.Manifest
import android.content.Intent
import android.content.pm.PackageManager
import android.location.LocationManager
import android.provider.Settings
import androidx.activity.compose.BackHandler
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.grid.GridCells
import androidx.compose.foundation.lazy.grid.LazyVerticalGrid
import androidx.compose.foundation.lazy.grid.items
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.animation.core.*
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.ui.focus.FocusRequester
import androidx.compose.ui.focus.focusRequester
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.*
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.*
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.painterResource
import androidx.core.content.ContextCompat
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.*
import androidx.navigation.NavController
import coil.compose.AsyncImage
import com.vkenterprises.vras.BuildConfig
import com.vkenterprises.vras.R
import com.vkenterprises.vras.data.models.SearchResult
import com.vkenterprises.vras.navigation.Screen
import com.vkenterprises.vras.viewmodel.AuthViewModel
import com.vkenterprises.vras.viewmodel.SearchMode
import com.vkenterprises.vras.viewmodel.SearchViewModel
import java.time.LocalDate
import java.time.temporal.ChronoUnit

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun HomeScreen(
    searchVm: SearchViewModel,
    authVm: AuthViewModel,
    nav: NavController
) {
    val ui          by searchVm.ui.collectAsState()
    val userId      by authVm.userId.collectAsState(initial = -1L)
    val userName    by authVm.userName.collectAsState(initial = "")
    val isAdmin     by authVm.isAdmin.collectAsState(initial = false)
    val kickReason  by authVm.kickReason.collectAsState()
    val agencyName  by authVm.agencyName.collectAsState(initial = null)
    val agencyLogo  by authVm.agencyLogo.collectAsState(initial = null)
    val subEnd      by authVm.subscriptionEnd.collectAsState(initial = null)

    // Back-press handling: if results are showing, back wipes them so the
    // dashboard (logo / contact / Remaining Days etc.) re-appears. Only on the
    // bare dashboard does back actually exit the app — matches user expectation.
    BackHandler(enabled = ui.results.isNotEmpty() || ui.inputText.isNotEmpty()) {
        searchVm.clearResults()
    }
    val agencyLogoUrl = agencyLogo
        ?.takeIf { it.isNotBlank() }
        ?.let { BuildConfig.BASE_URL.trimEnd('/') + "/" + it.trimStart('/') }
    val context    = LocalContext.current

    // Ask for location permission so the worker can send GPS heartbeats
    // ── Location gate ─────────────────────────────────────────────────────────
    // App cannot be used without location services enabled + permission granted.
    var showLocationDialog by remember { mutableStateOf(false) }
    // Holds the new online/offline state to confirm in the dialog.
    var pendingOnlineModeChange by remember { mutableStateOf<Boolean?>(null) }

    fun isLocationEnabled(): Boolean {
        val lm = context.getSystemService(android.content.Context.LOCATION_SERVICE) as LocationManager
        return lm.isProviderEnabled(LocationManager.GPS_PROVIDER) ||
               lm.isProviderEnabled(LocationManager.NETWORK_PROVIDER)
    }

    val locationPermLauncher = rememberLauncherForActivityResult(
        ActivityResultContracts.RequestMultiplePermissions()
    ) { grants ->
        val granted = grants.values.any { it }
        if (!granted || !isLocationEnabled()) showLocationDialog = true
    }

    LaunchedEffect(Unit) {
        val fine   = ContextCompat.checkSelfPermission(context, Manifest.permission.ACCESS_FINE_LOCATION)
        val coarse = ContextCompat.checkSelfPermission(context, Manifest.permission.ACCESS_COARSE_LOCATION)
        val permGranted = fine == PackageManager.PERMISSION_GRANTED || coarse == PackageManager.PERMISSION_GRANTED
        if (!permGranted) {
            locationPermLauncher.launch(arrayOf(
                Manifest.permission.ACCESS_FINE_LOCATION,
                Manifest.permission.ACCESS_COARSE_LOCATION
            ))
        } else if (!isLocationEnabled()) {
            showLocationDialog = true
        }
    }

    // Re-check every time we come back to the screen (user may have toggled GPS in settings)
    // Also refresh session so isAdmin / isActive changes from desktop take effect instantly.
    val lifecycleOwner = androidx.compose.ui.platform.LocalLifecycleOwner.current
    DisposableEffect(lifecycleOwner) {
        val obs = androidx.lifecycle.LifecycleEventObserver { _, event ->
            if (event == androidx.lifecycle.Lifecycle.Event.ON_RESUME) {
                val fine   = ContextCompat.checkSelfPermission(context, Manifest.permission.ACCESS_FINE_LOCATION)
                val coarse = ContextCompat.checkSelfPermission(context, Manifest.permission.ACCESS_COARSE_LOCATION)
                val ok = (fine == PackageManager.PERMISSION_GRANTED || coarse == PackageManager.PERMISSION_GRANTED) && isLocationEnabled()
                showLocationDialog = !ok
                authVm.refreshSession()
            }
        }
        lifecycleOwner.lifecycle.addObserver(obs)
        onDispose { lifecycleOwner.lifecycle.removeObserver(obs) }
    }

    // Online / Offline confirmation dialog — appears whenever the cloud icon
    // is tapped so the user explicitly acknowledges which mode they are in.
    pendingOnlineModeChange?.let { goingOnline ->
        AlertDialog(
            onDismissRequest = { pendingOnlineModeChange = null },
            icon = {
                Icon(
                    imageVector = if (goingOnline) Icons.Default.Cloud else Icons.Default.CloudOff,
                    contentDescription = null,
                    tint = if (goingOnline) Color(0xFF388E3C) else Color(0xFFD32F2F)
                )
            },
            title = {
                Text(
                    if (goingOnline) "Switch to Online?" else "Switch to Offline?",
                    fontWeight = FontWeight.Bold
                )
            },
            text = {
                Text(
                    if (goingOnline)
                        "You will be ONLINE. Vehicle searches will hit the live server and you will receive the latest records."
                    else
                        "You will be OFFLINE. Searches will only use the records already downloaded to this phone. Sync first if you want the latest data."
                )
            },
            confirmButton = {
                Button(
                    onClick = {
                        searchVm.setOnlineOnly(goingOnline)
                        pendingOnlineModeChange = null
                    },
                    colors = ButtonDefaults.buttonColors(
                        containerColor = if (goingOnline) Color(0xFF388E3C) else Color(0xFFD32F2F)
                    )
                ) {
                    Text(if (goingOnline) "Go Online" else "Go Offline", fontWeight = FontWeight.Bold)
                }
            },
            dismissButton = {
                TextButton(onClick = { pendingOnlineModeChange = null }) { Text("Cancel") }
            }
        )
    }

    if (showLocationDialog) {
        AlertDialog(
            onDismissRequest = { /* non-dismissible */ },
            icon = { Icon(Icons.Default.LocationOff, contentDescription = null,
                tint = MaterialTheme.colorScheme.error) },
            title = { Text("Location Required") },
            text  = { Text("This app requires location access to function. Please enable location services and grant location permission.") },
            confirmButton = {
                Button(onClick = {
                    val fine = ContextCompat.checkSelfPermission(context, Manifest.permission.ACCESS_FINE_LOCATION)
                    val coarse = ContextCompat.checkSelfPermission(context, Manifest.permission.ACCESS_COARSE_LOCATION)
                    if (fine != PackageManager.PERMISSION_GRANTED && coarse != PackageManager.PERMISSION_GRANTED) {
                        locationPermLauncher.launch(arrayOf(
                            Manifest.permission.ACCESS_FINE_LOCATION,
                            Manifest.permission.ACCESS_COARSE_LOCATION))
                    } else {
                        context.startActivity(Intent(Settings.ACTION_LOCATION_SOURCE_SETTINGS))
                    }
                }) { Text("Enable Location") }
            }
        )
    }

    LaunchedEffect(ui.subscriptionExpired) {
        if (ui.subscriptionExpired) {
            searchVm.resetBlockedStates()
            nav.navigate(Screen.SubscriptionExpired.route) {
                popUpTo(Screen.Home.route) { inclusive = true }
            }
        }
    }

    LaunchedEffect(ui.appStopped) {
        if (ui.appStopped) {
            searchVm.resetBlockedStates()
            nav.navigate(Screen.AppStopped.route) {
                popUpTo(Screen.Home.route) { inclusive = true }
            }
        }
    }

    LaunchedEffect(ui.blacklisted) {
        if (ui.blacklisted) {
            searchVm.resetBlockedStates()
            nav.navigate(Screen.Blacklisted.route) {
                popUpTo(Screen.Home.route) { inclusive = true }
            }
        }
    }

    // Start foreground status polling (every 10 s via viewModelScope)
    LaunchedEffect(userId) {
        if (userId > 0) authVm.startStatusPolling(userId)
    }

    // Instant reaction to stop/blacklist detected by polling
    LaunchedEffect(kickReason) {
        val reason = kickReason ?: return@LaunchedEffect
        when (reason) {
            "app_stopped" -> nav.navigate(Screen.AppStopped.route)  { popUpTo(Screen.Home.route) { inclusive = true } }
            "blacklisted" -> nav.navigate(Screen.Blacklisted.route) { popUpTo(Screen.Home.route) { inclusive = true } }
        }
    }

    LaunchedEffect(ui.inactive) {
        if (ui.inactive) {
            searchVm.resetBlockedStates()
            nav.navigate(Screen.Inactive.route) {
                popUpTo(Screen.Home.route) { inclusive = true }
            }
        }
    }

    Scaffold(
        topBar = {
            TopAppBar(
                title = {
                    Row(verticalAlignment = Alignment.CenterVertically) {
                        // White-background card so the logo (which may have
                        // any colours / transparency) is always legible.
                        Surface(
                            shape = RoundedCornerShape(8.dp),
                            color = Color.White,
                            tonalElevation = 0.dp,
                            shadowElevation = 0.dp,
                            border = androidx.compose.foundation.BorderStroke(
                                1.dp, MaterialTheme.colorScheme.outlineVariant
                            ),
                            modifier = Modifier.size(38.dp)
                        ) {
                            // Logo is baked into the per-flavor APK at build time
                            // (see android/tools/gen_flavors.py). No network fetch,
                            // no CRMRS fallback — every white-label build ships its
                            // agency's actual logo.
                            Image(
                                painter = painterResource(id = R.drawable.agency_logo),
                                contentDescription = BuildConfig.AGENCY_NAME,
                                contentScale = ContentScale.Fit,
                                modifier = Modifier.fillMaxSize().padding(3.dp)
                            )
                        }
                        Spacer(Modifier.width(10.dp))
                        Column {
                            Text(
                                agencyName ?: "Agency",
                                fontWeight = FontWeight.Bold,
                                fontSize = 15.sp,
                                maxLines = 1,
                                overflow = TextOverflow.Ellipsis
                            )
                            if (userName.isNotEmpty())
                                Text("Hello, $userName",
                                    style = MaterialTheme.typography.bodySmall,
                                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                                    maxLines = 1, overflow = TextOverflow.Ellipsis)
                        }
                    }
                },
                actions = {
                    val pfpUrl by authVm.pfpUrl.collectAsState(initial = null)
                    // The standalone "Manage Subscriptions" icon was removed —
                    // admins reach the same screen via Control Panel below.

                    // Online / Offline toggle — green cloud when online (default),
                    // red cloud-off when offline. Tap shows a confirmation dialog
                    // so the user always knows which mode they are in.
                    IconButton(onClick = { pendingOnlineModeChange = !ui.onlineOnly }) {
                        Icon(
                            imageVector       = if (ui.onlineOnly) Icons.Default.Cloud
                                                else                Icons.Default.CloudOff,
                            contentDescription = if (ui.onlineOnly) "Online mode (tap to go offline)"
                                                 else                "Offline mode (tap to go online)",
                            tint              = if (ui.onlineOnly) Color(0xFF388E3C)
                                                else                Color(0xFFD32F2F)
                        )
                    }
                    val syncHasUpdates = ui.syncHasUpdates
                    val syncCompleted  = ui.syncCompleted
                    val infiniteTransition = rememberInfiniteTransition(label = "syncPulse")
                    val pulseAlpha by infiniteTransition.animateFloat(
                        initialValue = 1f, targetValue = 0.3f,
                        animationSpec = infiniteRepeatable(
                            animation = tween(600, easing = FastOutSlowInEasing),
                            repeatMode = RepeatMode.Reverse
                        ), label = "pulseAlpha"
                    )
                    val syncIconTint = when {
                        ui.isSyncing      -> MaterialTheme.colorScheme.primary
                        syncHasUpdates    -> Color(0xFFD32F2F).copy(alpha = pulseAlpha)
                        syncCompleted     -> Color(0xFF388E3C)
                        else              -> MaterialTheme.colorScheme.onSurface
                    }
                    val syncIcon = if (syncCompleted && !syncHasUpdates)
                        Icons.Default.CheckCircle else Icons.Default.CloudDownload
                    IconButton(onClick = { searchVm.triggerSync() }) {
                        Icon(syncIcon, contentDescription = "Download records", tint = syncIconTint)
                    }
                    IconButton(onClick = { nav.navigate(Screen.Profile.route) }) {
                        if (!pfpUrl.isNullOrBlank()) {
                            // Coil request: account-circle icon shown both as
                            // "loading" placeholder and "error" fallback, so the
                            // avatar slot never renders as a broken/empty box.
                            // crossfade for a smooth swap once the bytes land.
                            val req = coil.request.ImageRequest.Builder(LocalContext.current)
                                .data(pfpUrl)
                                .crossfade(true)
                                .build()
                            AsyncImage(
                                model = req,
                                contentDescription = null,
                                contentScale = ContentScale.Crop,
                                modifier = Modifier.size(30.dp).clip(CircleShape)
                            )
                        } else Icon(Icons.Default.AccountCircle, null)
                    }
                    IconButton(onClick = { nav.navigate(Screen.Settings.route) }) {
                        Icon(Icons.Default.Settings, contentDescription = "Settings")
                    }
                },
                colors = TopAppBarDefaults.topAppBarColors(
                    containerColor = MaterialTheme.colorScheme.surface)
            )
        }
    ) { pad ->
        Column(Modifier.padding(pad).fillMaxSize()) {

            // Sync progress banner
            if (ui.isSyncing) {
                val pct = if (ui.syncTotal > 0) (ui.syncCurrent.toFloat() / ui.syncTotal) else 0f
                Surface(color = MaterialTheme.colorScheme.primaryContainer) {
                    Column(Modifier.fillMaxWidth().padding(horizontal = 12.dp, vertical = 6.dp)) {
                        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween) {
                            Text("Syncing vehicle data…",
                                style = MaterialTheme.typography.labelSmall,
                                color = MaterialTheme.colorScheme.onPrimaryContainer)
                            Text("${ui.syncCurrent.toInt().formatCount()} / ${ui.syncTotal.toInt().formatCount()}",
                                style = MaterialTheme.typography.labelSmall,
                                color = MaterialTheme.colorScheme.onPrimaryContainer)
                        }
                        Spacer(Modifier.height(4.dp))
                        LinearProgressIndicator(
                            progress = { pct },
                            modifier = Modifier.fillMaxWidth(),
                            color = MaterialTheme.colorScheme.primary,
                            trackColor = MaterialTheme.colorScheme.primary.copy(alpha = 0.2f)
                        )
                    }
                }
            }

            // ── Search bar ───────────────────────────────────────────────
            Surface(
                color = MaterialTheme.colorScheme.surfaceVariant,
                modifier = Modifier.fillMaxWidth()
            ) {
                Column(Modifier.padding(horizontal = 10.dp, vertical = 6.dp)) {
                    val maxLen = if (ui.mode == SearchMode.RC) 4 else 5
                    val focusRequester = remember { FocusRequester() }
                    LaunchedEffect(Unit) {
                        focusRequester.requestFocus()
                    }
                    OutlinedTextField(
                        value = ui.inputText,
                        onValueChange = { searchVm.onInputChange(it, userId) },
                        placeholder = {
                            Text(
                                if (ui.mode == SearchMode.RC) "Enter last 4 digits of RC"
                                else "Enter last 5 digits of Chassis",
                                style = MaterialTheme.typography.bodySmall
                            )
                        },
                        leadingIcon = { Icon(Icons.Default.Search, null, Modifier.size(18.dp)) },
                        trailingIcon = {
                            Surface(
                                shape = RoundedCornerShape(50),
                                color = MaterialTheme.colorScheme.primary,
                                modifier = Modifier
                                    .padding(end = 6.dp)
                                    .clickable {
                                        searchVm.setMode(
                                            if (ui.mode == SearchMode.RC) SearchMode.CHASSIS
                                            else SearchMode.RC
                                        )
                                    }
                            ) {
                                Row(
                                    Modifier.padding(horizontal = 12.dp, vertical = 6.dp),
                                    verticalAlignment = Alignment.CenterVertically,
                                    horizontalArrangement = Arrangement.spacedBy(5.dp)
                                ) {
                                    Text(
                                        if (ui.mode == SearchMode.RC) "RC" else "CH",
                                        color = MaterialTheme.colorScheme.onPrimary,
                                        fontWeight = FontWeight.Bold,
                                        fontSize = 12.sp,
                                        letterSpacing = 0.5.sp
                                    )
                                    Icon(
                                        Icons.Default.SwapHoriz, "Switch mode",
                                        tint = MaterialTheme.colorScheme.onPrimary,
                                        modifier = Modifier.size(14.dp)
                                    )
                                }
                            }
                        },
                        keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
                        singleLine = true,
                        modifier = Modifier.fillMaxWidth().focusRequester(focusRequester),
                        shape = RoundedCornerShape(8.dp),
                        textStyle = MaterialTheme.typography.bodyMedium.copy(
                            fontFamily = FontFamily.Monospace,
                            fontWeight = FontWeight.Bold,
                            letterSpacing = 3.sp
                        ),
                        colors = OutlinedTextFieldDefaults.colors(
                            focusedContainerColor   = MaterialTheme.colorScheme.surface,
                            unfocusedContainerColor = MaterialTheme.colorScheme.surface
                        )
                    )
                }
            }

            // ── Error ────────────────────────────────────────────────────
            if (ui.errorMsg != null) {
                Surface(color = MaterialTheme.colorScheme.errorContainer) {
                    Text(ui.errorMsg!!, Modifier.fillMaxWidth().padding(10.dp),
                        style = MaterialTheme.typography.bodySmall,
                        color = MaterialTheme.colorScheme.onErrorContainer)
                }
            }

            // ── Results ──────────────────────────────────────────────────
            if (ui.results.isNotEmpty()) {
                // Count bar — thin strip (kept small to give the list more room)
                Surface(color = MaterialTheme.colorScheme.primaryContainer) {
                    Row(
                        Modifier.fillMaxWidth().padding(horizontal = 12.dp, vertical = 2.dp),
                        horizontalArrangement = Arrangement.SpaceBetween,
                        verticalAlignment = Alignment.CenterVertically
                    ) {
                        Text(
                            "${ui.results.size} record(s) found",
                            style = MaterialTheme.typography.labelSmall,
                            fontWeight = FontWeight.SemiBold,
                            color = MaterialTheme.colorScheme.onPrimaryContainer
                        )
                        if (ui.lastQuery.isNotBlank()) {
                            Text(
                                "\"${ui.lastQuery}\"",
                                style = MaterialTheme.typography.labelSmall,
                                fontFamily = FontFamily.Monospace,
                                color = MaterialTheme.colorScheme.onPrimaryContainer.copy(alpha = 0.7f)
                            )
                        }
                    }
                }

                if (ui.twoColumnView) {
                    // Column-wise alphabetical order: col1 = first half, col2 = second half.
                    // remember() keyed on ui.results means we only recompute the reorder
                    // when the result list changes — not on every unrelated recomposition.
                    val reordered = remember(ui.results) {
                        val sorted = ui.results
                        val half   = (sorted.size + 1) / 2
                        buildList(sorted.size) {
                            for (i in 0 until half) {
                                add(sorted[i])
                                if (half + i < sorted.size) add(sorted[half + i])
                            }
                        }
                    }
                    LazyVerticalGrid(
                        columns = GridCells.Fixed(2),
                        modifier = Modifier.fillMaxSize(),
                        contentPadding = PaddingValues(0.dp)
                    ) {
                        // key = id lets Compose diff items across searches and reuse
                        // composition slots for rows that didn't change — big win on
                        // partial-result updates.
                        items(reordered, key = { it.id }) { item ->
                            VehicleGridCell(item, ui.mode) {
                                searchVm.selectResult(item)
                                nav.navigate(Screen.VehicleDetail.route)
                            }
                        }
                    }
                } else {
                    LazyColumn(modifier = Modifier.fillMaxSize()) {
                        items(ui.results, key = { it.id }) { item ->
                            VehicleListRow(item, ui.mode) {
                                searchVm.selectResult(item)
                                nav.navigate(Screen.VehicleDetail.route)
                            }
                        }
                    }
                }
            } else if (ui.isSearching) {
                // First search of a session: the server can take a moment to warm
                // up, so show a clear busy state instead of flashing the dashboard.
                Box(Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
                    Column(
                        horizontalAlignment = Alignment.CenterHorizontally,
                        verticalArrangement  = Arrangement.spacedBy(14.dp)
                    ) {
                        CircularProgressIndicator()
                        Text(
                            "Searching…",
                            style = MaterialTheme.typography.bodyMedium,
                            color = MaterialTheme.colorScheme.onSurfaceVariant
                        )
                    }
                }
            } else if (ui.errorMsg == null) {
                AgencyLandingPanel(
                    agencyName    = BuildConfig.AGENCY_NAME,
                    agencyMobile  = BuildConfig.AGENCY_MOBILE,
                    agencyAddress = BuildConfig.AGENCY_ADDRESS,
                    agencyLogoUrl = agencyLogoUrl,
                    subEndDate    = subEnd,
                    offlineCount  = ui.offlineCount,
                    isAdmin       = isAdmin,
                    nav           = nav
                )
            }
        }
    }
}

// ── Idle-state landing panel ─────────────────────────────────────────────────
// Shown beneath the search bar when there are no results. Replaces the old
// 2-tile quick-access grid. Layout per request:
//   logo → mobile → address → Remaining Days / Offline Records / My Account
//   (+ Control Panel for admins) → "Software designed by CRMRS" footer.
@Composable
private fun AgencyLandingPanel(
    agencyName: String,
    agencyMobile: String,
    agencyAddress: String,
    agencyLogoUrl: String?,
    subEndDate: String?,
    offlineCount: Long,
    isAdmin: Boolean,
    nav: NavController,
) {
    val daysLeft = remember(subEndDate) {
        if (subEndDate.isNullOrBlank()) null
        else runCatching {
            ChronoUnit.DAYS.between(LocalDate.now(), LocalDate.parse(subEndDate))
                .coerceAtLeast(0L)
        }.getOrNull()
    }
    Column(
        Modifier.fillMaxSize().padding(horizontal = 20.dp, vertical = 18.dp),
        horizontalAlignment = Alignment.CenterHorizontally
    ) {
        // Big agency logo on a white card
        Surface(
            shape = RoundedCornerShape(20.dp),
            color = Color.White,
            tonalElevation = 0.dp,
            shadowElevation = 2.dp,
            border = androidx.compose.foundation.BorderStroke(
                1.dp, MaterialTheme.colorScheme.outlineVariant
            ),
            modifier = Modifier.size(100.dp)
        ) {
            Image(
                painter = painterResource(id = R.drawable.agency_logo),
                contentDescription = agencyName,
                contentScale = ContentScale.Fit,
                modifier = Modifier.fillMaxSize().padding(8.dp)
            )
        }
        Spacer(Modifier.height(14.dp))
        Text(agencyName,
            style = MaterialTheme.typography.titleMedium,
            fontWeight = FontWeight.Bold,
            textAlign = TextAlign.Center)
        Spacer(Modifier.height(6.dp))
        if (agencyMobile.isNotBlank()) {
            Row(verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                Icon(Icons.Default.Phone, null,
                    tint = MaterialTheme.colorScheme.primary,
                    modifier = Modifier.size(16.dp))
                Text(agencyMobile,
                    style = MaterialTheme.typography.bodyMedium,
                    fontWeight = FontWeight.SemiBold)
            }
        }
        if (agencyAddress.isNotBlank()) {
            Spacer(Modifier.height(4.dp))
            Row(verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                Icon(Icons.Default.Place, null,
                    tint = MaterialTheme.colorScheme.outline,
                    modifier = Modifier.size(16.dp))
                Text(agencyAddress,
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                    textAlign = TextAlign.Center,
                    maxLines = 2,
                    overflow = TextOverflow.Ellipsis)
            }
        }
        Spacer(Modifier.height(22.dp))

        LandingTile(
            label    = "REMAINING DAYS",
            icon     = Icons.Default.Schedule,
            subtitle = when (daysLeft) {
                null -> "No active subscription"
                0L   -> "Expires today"
                1L   -> "1 day left"
                else -> "$daysLeft days left"
            },
            accent   = when {
                daysLeft == null  -> MaterialTheme.colorScheme.error
                daysLeft <= 3L    -> Color(0xFFD32F2F)
                daysLeft <= 7L    -> Color(0xFFEF6C00)
                else              -> Color(0xFF388E3C)
            }
        ) { /* read-only tile */ }
        Spacer(Modifier.height(10.dp))
        LandingTile(
            label    = "OFFLINE RECORDS",
            icon     = Icons.Default.CloudDownload,
            // Record count intentionally hidden — agents shouldn't see how many
            // records are on the device.
            subtitle = "Saved on this phone",
            accent   = MaterialTheme.colorScheme.primary
        ) { nav.navigate(Screen.Settings.route) }
        Spacer(Modifier.height(10.dp))
        LandingTile(
            label    = "MY ACCOUNT",
            icon     = Icons.Default.AccountCircle,
            subtitle = "View profile, KYC and subscriptions",
            accent   = MaterialTheme.colorScheme.primary
        ) { nav.navigate(Screen.Profile.route) }
        if (isAdmin) {
            Spacer(Modifier.height(10.dp))
            LandingTile(
                label    = "CONTROL PANEL",
                icon     = Icons.Default.Lock,
                subtitle = "Manage users, subscriptions, search logs",
                accent   = Color(0xFF6A1B9A)
            ) { nav.navigate(Screen.ControlPanel.route) }
        }
        Spacer(Modifier.weight(1f))
        Spacer(Modifier.height(12.dp))
        Text("SOFTWARE DESIGNED BY",
            style = MaterialTheme.typography.labelSmall,
            color = MaterialTheme.colorScheme.outline,
            letterSpacing = 1.5.sp)
        Text("CRMRS",
            style = MaterialTheme.typography.titleSmall,
            fontWeight = FontWeight.ExtraBold,
            color = MaterialTheme.colorScheme.primary,
            letterSpacing = 2.sp)
        Text("rahul@loopwar.dev",
            style = MaterialTheme.typography.labelSmall,
            color = MaterialTheme.colorScheme.outline)
        Spacer(Modifier.height(8.dp))
    }
}

@Composable
private fun LandingTile(
    label: String,
    icon: androidx.compose.ui.graphics.vector.ImageVector,
    subtitle: String,
    accent: Color,
    onClick: () -> Unit
) {
    Card(
        onClick = onClick,
        shape = RoundedCornerShape(14.dp),
        colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.surfaceVariant),
        modifier = Modifier.fillMaxWidth()
    ) {
        Row(
            Modifier.padding(horizontal = 16.dp, vertical = 14.dp),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(14.dp)
        ) {
            Surface(
                shape = RoundedCornerShape(10.dp),
                color = accent.copy(alpha = 0.15f),
                modifier = Modifier.size(40.dp)
            ) {
                Box(contentAlignment = Alignment.Center) {
                    Icon(icon, null, tint = accent, modifier = Modifier.size(22.dp))
                }
            }
            Column(Modifier.weight(1f)) {
                Text(label,
                    style = MaterialTheme.typography.labelLarge,
                    fontWeight = FontWeight.Bold,
                    letterSpacing = 0.8.sp)
                Text(subtitle,
                    style = MaterialTheme.typography.labelSmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant)
            }
            Icon(Icons.Default.ChevronRight, null,
                tint = MaterialTheme.colorScheme.outline,
                modifier = Modifier.size(20.dp))
        }
    }
}

@Composable
private fun VehicleGridCell(item: SearchResult, mode: SearchMode, onClick: () -> Unit) {
    val display = when (mode) {
        SearchMode.RC      -> item.vehicleNo.ifBlank { "—" }
        SearchMode.CHASSIS -> item.chassisNo.ifBlank { "—" }
    }
    val isInvalidRc = mode == SearchMode.RC && item.vehicleNo.isNotBlank() && !item.vehicleNo.isValidRc()
    // Compact rows so ~21 fit per column in a single look (matches the
    // reference's tight 2-column list).
    Row(
        Modifier
            .fillMaxWidth()
            .clickable(onClick = onClick)
            // Big bold RC, but almost no row padding so ~21 fit per column —
            // the height comes from the text itself, not whitespace.
            .padding(start = 12.dp, end = 6.dp, top = 3.dp, bottom = 3.dp),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.SpaceBetween
    ) {
        Text(
            display,
            // Match the reference exactly: roboto_bold = FontWeight.Bold (700),
            // big and clean (not ultra-black). Default sans family.
            fontWeight = FontWeight.Bold,
            fontFamily = FontFamily.Default,
            fontSize = 16.sp,
            lineHeight = 18.sp,
            maxLines = 1,
            overflow = TextOverflow.Ellipsis,
            modifier = Modifier.weight(1f),
            color = if (isInvalidRc) MaterialTheme.colorScheme.error
                    else MaterialTheme.colorScheme.onSurface
        )
        if (isInvalidRc) {
            Text("Invalid",
                style = MaterialTheme.typography.labelSmall,
                color = MaterialTheme.colorScheme.error,
                modifier = Modifier.padding(end = 4.dp))
        } else {
            Icon(Icons.Default.ChevronRight, null,
                Modifier.size(12.dp),
                tint = MaterialTheme.colorScheme.outlineVariant)
        }
    }
    HorizontalDivider(
        color = MaterialTheme.colorScheme.outlineVariant.copy(alpha = 0.3f),
        thickness = 0.5.dp
    )
}

@Composable
private fun HomeTile(
    label: String,
    icon: androidx.compose.ui.graphics.vector.ImageVector,
    modifier: Modifier = Modifier,
    subtitle: String? = null,
    onClick: () -> Unit
) {
    Card(
        onClick = onClick,
        shape = RoundedCornerShape(12.dp),
        colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.surfaceVariant),
        modifier = modifier
    ) {
        Row(
            Modifier.padding(16.dp),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(10.dp)
        ) {
            Icon(icon, null, tint = MaterialTheme.colorScheme.primary)
            Column {
                Text(label, fontWeight = FontWeight.SemiBold,
                    style = MaterialTheme.typography.bodyMedium)
                if (!subtitle.isNullOrBlank()) {
                    Text(
                        subtitle,
                        style      = MaterialTheme.typography.labelSmall,
                        color      = MaterialTheme.colorScheme.onSurfaceVariant,
                        fontWeight = FontWeight.Medium
                    )
                }
            }
        }
    }
}

// 1,234 → "1,234" / 12345 → "12,345" / 1234567 → "1,234,567"
private fun formatRoomCount(n: Long): String =
    java.text.NumberFormat.getInstance(java.util.Locale.US).format(n)

@Composable
private fun ModeTab(label: String, selected: Boolean, onClick: () -> Unit, modifier: Modifier) {
    Box(
        modifier.clip(RoundedCornerShape(6.dp))
            .background(
                if (selected) MaterialTheme.colorScheme.primary
                else MaterialTheme.colorScheme.surface
            )
            .clickable(onClick = onClick)
            .padding(vertical = 8.dp),
        contentAlignment = Alignment.Center
    ) {
        Text(label,
            style = MaterialTheme.typography.labelMedium,
            fontWeight = if (selected) FontWeight.Bold else FontWeight.Normal,
            color = if (selected) MaterialTheme.colorScheme.onPrimary
                    else MaterialTheme.colorScheme.onSurfaceVariant)
    }
}

@Composable
private fun VehicleListRow(item: SearchResult, mode: SearchMode, onClick: () -> Unit) {
    val rcOrChassis = when (mode) {
        SearchMode.RC      -> item.vehicleNo.ifBlank { "—" }
        SearchMode.CHASSIS -> item.chassisNo.ifBlank { "—" }
    }
    val model = item.model.ifBlank { "—" }
    Row(
        Modifier
            .fillMaxWidth()
            .clickable(onClick = onClick)
            .padding(horizontal = 12.dp, vertical = 10.dp),
        verticalAlignment = Alignment.CenterVertically
    ) {
        Text(
            rcOrChassis,
            style = MaterialTheme.typography.bodyLarge,
            fontWeight = FontWeight.Black,
            fontFamily = FontFamily.Default,
            maxLines = 1,
            overflow = TextOverflow.Ellipsis,
            modifier = Modifier.weight(1f)
        )
        Text(
            model,
            style = MaterialTheme.typography.bodyMedium,
            fontWeight = FontWeight.Bold,
            fontFamily = FontFamily.SansSerif,
            maxLines = 1,
            overflow = TextOverflow.Ellipsis,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
            modifier = Modifier.weight(1f)
        )
        Icon(
            Icons.Default.ChevronRight, null,
            Modifier.size(14.dp),
            tint = MaterialTheme.colorScheme.outlineVariant
        )
    }
    HorizontalDivider(
        color = MaterialTheme.colorScheme.outlineVariant.copy(alpha = 0.3f),
        thickness = 0.5.dp
    )
}

private fun Int.formatCount(): String = when {
    this >= 1_000_000 -> "${this / 1_000_000}.${(this % 1_000_000) / 100_000}M"
    this >= 1_000     -> "${this / 1_000}K"
    else              -> "$this"
}

// Indian RC formats accepted:
//   * Standard:    MH12AB1234   — 2 state + 2 district + 1-3 series + 4 unique
//   * Legacy long: HR736546     — 2 state + 5-7 digits (govt / older)
//   * Bharat (BH): 22BH2271E    — 2 year + BH + 4 digits + 1-2 letters
private val RC_REGEX = Regex(
    "^([A-Z]{2}[0-9]{2}[A-Z]{1,3}[0-9]{4}|[A-Z]{2}[0-9]{5,7}|[0-9]{2}BH[0-9]{4}[A-Z]{1,2})$"
)
private fun String.isValidRc(): Boolean =
    replace(Regex("[^A-Z0-9]"), "").uppercase().matches(RC_REGEX)

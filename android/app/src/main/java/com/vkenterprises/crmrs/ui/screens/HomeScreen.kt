package com.vkenterprises.crmrs.ui.screens

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
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.rememberLazyListState
import androidx.compose.foundation.lazy.grid.GridCells
import androidx.compose.foundation.lazy.grid.LazyVerticalGrid
import androidx.compose.foundation.lazy.grid.items
import androidx.compose.foundation.lazy.grid.rememberLazyGridState
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
import androidx.compose.ui.text.font.Font
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.*
import androidx.navigation.NavController
import coil.compose.AsyncImage
import com.vkenterprises.crmrs.BuildConfig
import com.vkenterprises.crmrs.R
import com.vkenterprises.crmrs.data.models.SearchResult
import com.vkenterprises.crmrs.navigation.Screen
import com.vkenterprises.crmrs.viewmodel.AuthViewModel
import com.vkenterprises.crmrs.viewmodel.SearchMode
import com.vkenterprises.crmrs.viewmodel.SearchViewModel
import java.time.LocalDate
import java.time.temporal.ChronoUnit

private val RobotoFamily = FontFamily(
    Font(R.font.roboto_bold,  FontWeight.Bold),
    Font(R.font.roboto_black, FontWeight.Black)
)

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun HomeScreen(
    searchVm: SearchViewModel,
    authVm: AuthViewModel,
    repoVm: com.vkenterprises.crmrs.viewmodel.RepoViewModel,
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

    BackHandler(enabled = ui.results.isNotEmpty() || ui.inputText.isNotEmpty()) {
        searchVm.clearResults()
    }
    val agencyLogoUrl = agencyLogo
        ?.takeIf { it.isNotBlank() }
        ?.let { BuildConfig.BASE_URL.trimEnd('/') + "/" + it.trimStart('/') }
    val context    = LocalContext.current

    var showLocationDialog by remember { mutableStateOf(false) }
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
            onDismissRequest = { },
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

    LaunchedEffect(userId) {
        if (userId > 0) authVm.startStatusPolling(userId)
    }

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

            if (ui.errorMsg != null) {
                Surface(color = MaterialTheme.colorScheme.errorContainer) {
                    Text(ui.errorMsg!!, Modifier.fillMaxWidth().padding(10.dp),
                        style = MaterialTheme.typography.bodySmall,
                        color = MaterialTheme.colorScheme.onErrorContainer)
                }
            }

            val gridState = rememberLazyGridState()
            val listState = rememberLazyListState()
            LaunchedEffect(ui.lastQuery) {
                gridState.scrollToItem(0)
                listState.scrollToItem(0)
            }

            if (ui.results.isNotEmpty()) {
                Surface(color = MaterialTheme.colorScheme.primaryContainer) {
                    Row(
                        Modifier.fillMaxWidth().padding(horizontal = 12.dp, vertical = 1.dp),
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
                        state = gridState,
                        modifier = Modifier.fillMaxSize(),
                        contentPadding = PaddingValues(0.dp)
                    ) {
                        items(reordered, key = { it.id }) { item ->
                            VehicleGridCell(item, ui.mode) {
                                searchVm.selectResult(item)
                                nav.navigate(Screen.VehicleDetail.route)
                            }
                        }
                    }
                } else {
                    LazyColumn(state = listState, modifier = Modifier.fillMaxSize()) {
                        items(ui.results, key = { it.id }) { item ->
                            VehicleListRow(item, ui.mode) {
                                searchVm.selectResult(item)
                                nav.navigate(Screen.VehicleDetail.route)
                            }
                        }
                    }
                }
            } else if (ui.isSearching) {
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
                    nav           = nav,
                    onOpenLetters = {
                        repoVm.setFlow(com.vkenterprises.crmrs.viewmodel.RepoFlow.LETTER)
                        if (userId > 0) repoVm.loadHeadOffices(userId)
                        nav.navigate(Screen.RepoType.route)
                    },
                    onOpenBilling = {
                        repoVm.setFlow(com.vkenterprises.crmrs.viewmodel.RepoFlow.BILLING)
                        if (userId > 0) { repoVm.loadHeadOffices(userId); repoVm.loadBillingSettings(userId) }
                        nav.navigate(Screen.RepoHeadOffices.route)
                    }
                )
            }
        }
    }
}

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
    onOpenLetters: () -> Unit,
    onOpenBilling: () -> Unit,
) {
    val daysLeft = remember(subEndDate) {
        if (subEndDate.isNullOrBlank()) null
        else runCatching {
            ChronoUnit.DAYS.between(LocalDate.now(), LocalDate.parse(subEndDate))
                .coerceAtLeast(0L)
        }.getOrNull()
    }
    Column(
        Modifier.fillMaxSize().verticalScroll(rememberScrollState())
            .padding(horizontal = 20.dp, vertical = 18.dp),
        horizontalAlignment = Alignment.CenterHorizontally
    ) {
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
        ) { }
        Spacer(Modifier.height(10.dp))
        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(10.dp)) {
            GridTile(
                label    = "OFFLINE RECORDS",
                icon     = Icons.Default.CloudDownload,
                subtitle = "Saved on this phone",
                accent   = MaterialTheme.colorScheme.primary,
                modifier = Modifier.weight(1f)
            ) { nav.navigate(Screen.Settings.route) }
            GridTile(
                label    = "MY ACCOUNT",
                icon     = Icons.Default.AccountCircle,
                subtitle = "Profile, KYC, subscriptions",
                accent   = MaterialTheme.colorScheme.primary,
                modifier = Modifier.weight(1f)
            ) { nav.navigate(Screen.Profile.route) }
        }
        if (isAdmin) {
            Spacer(Modifier.height(10.dp))
            Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                GridTile(
                    label    = "PRE POST INTIMATION",
                    icon     = Icons.Default.Description,
                    subtitle = "Generate Pre / Post letter",
                    accent   = Color(0xFFF57F17),
                    modifier = Modifier.weight(1f)
                ) { onOpenLetters() }
                GridTile(
                    label    = "BILLING",
                    icon     = Icons.Default.ReceiptLong,
                    subtitle = "Generate repossession bill",
                    accent   = Color(0xFF00897B),
                    modifier = Modifier.weight(1f)
                ) { onOpenBilling() }
            }
            Spacer(Modifier.height(10.dp))
            Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                GridTile(
                    label    = "CONTROL PANEL",
                    icon     = Icons.Default.Lock,
                    subtitle = "Users, subscriptions, logs",
                    accent   = Color(0xFF6A1B9A),
                    modifier = Modifier.weight(1f)
                ) { nav.navigate(Screen.ControlPanel.route) }
                Spacer(Modifier.weight(1f))
            }
        }
        Spacer(Modifier.height(24.dp))
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
private fun GridTile(
    label: String,
    icon: androidx.compose.ui.graphics.vector.ImageVector,
    subtitle: String,
    accent: Color,
    modifier: Modifier = Modifier,
    onClick: () -> Unit
) {
    Card(
        onClick = onClick,
        shape = RoundedCornerShape(14.dp),
        colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.surfaceVariant),
        modifier = modifier.height(120.dp)
    ) {
        Column(
            Modifier.fillMaxSize().padding(14.dp),
            verticalArrangement = Arrangement.spacedBy(8.dp)
        ) {
            Surface(
                shape = RoundedCornerShape(10.dp),
                color = accent.copy(alpha = 0.15f),
                modifier = Modifier.size(38.dp)
            ) {
                Box(contentAlignment = Alignment.Center) {
                    Icon(icon, null, tint = accent, modifier = Modifier.size(20.dp))
                }
            }
            Text(
                label,
                style = MaterialTheme.typography.labelLarge,
                fontWeight = FontWeight.Bold,
                maxLines = 2,
                overflow = TextOverflow.Ellipsis,
                lineHeight = 16.sp
            )
            Text(
                subtitle,
                style = MaterialTheme.typography.labelSmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis
            )
        }
    }
}

@Composable
private fun LandingTile(
    label: String,
    icon: androidx.compose.ui.graphics.vector.ImageVector,
    subtitle: String,
    accent: Color,
    modifier: Modifier = Modifier.fillMaxWidth(),
    onClick: () -> Unit
) {
    Card(
        onClick = onClick,
        shape = RoundedCornerShape(14.dp),
        colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.surfaceVariant),
        modifier = modifier
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
    Row(
        Modifier
            .fillMaxWidth()
            .clickable(onClick = onClick)
            .padding(start = 10.dp, end = 2.dp, top = 8.dp, bottom = 8.dp),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.SpaceBetween
    ) {
        Text(
            display,
            fontWeight = FontWeight.Bold,
            fontFamily = RobotoFamily,
            fontSize = 16.sp,
            lineHeight = 18.sp,
            maxLines = 1,
            softWrap = false,
            overflow = TextOverflow.Clip,
            modifier = Modifier.weight(1f),
            color = if (isInvalidRc) MaterialTheme.colorScheme.error
                    else MaterialTheme.colorScheme.onSurface
        )
        if (isInvalidRc) {
            Text("!",
                style = MaterialTheme.typography.labelMedium,
                fontWeight = FontWeight.Bold,
                color = MaterialTheme.colorScheme.error,
                modifier = Modifier.padding(horizontal = 2.dp))
        } else {
            Icon(Icons.Default.ChevronRight, null,
                Modifier.size(14.dp),
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

private val RC_REGEX = Regex(
    "^([A-Z]{2}[0-9]{2}[A-Z]{1,3}[0-9]{4}|[A-Z]{2}[0-9]{5,7}|[0-9]{2}BH[0-9]{4}[A-Z]{1,2})$"
)
private fun String.isValidRc(): Boolean =
    replace(Regex("[^A-Z0-9]"), "").uppercase().matches(RC_REGEX)

package com.vkenterprises.vras.ui.screens

import android.annotation.SuppressLint
import android.content.Context
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.*
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.*
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.*
import androidx.navigation.NavController
import com.google.android.gms.location.LocationServices
import com.google.android.gms.location.Priority
import com.vkenterprises.vras.data.api.ApiClient
import com.vkenterprises.vras.data.models.SearchLogRequest
import com.vkenterprises.vras.navigation.Screen
import com.vkenterprises.vras.viewmodel.AuthViewModel
import com.vkenterprises.vras.viewmodel.SearchViewModel
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.suspendCancellableCoroutine
import kotlinx.coroutines.withContext
import kotlin.coroutines.resume

private val RC_REGEX = Regex("^[A-Z]{2}[0-9]{2}[A-Z]{1,3}[0-9]{4}$")
private fun String.isValidRc(): Boolean =
    replace(Regex("[^A-Z0-9]"), "").uppercase().matches(RC_REGEX)

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun VehicleDetailScreen(
    searchVm: SearchViewModel,
    authVm: AuthViewModel,
    nav: NavController
) {
    val ui         by searchVm.ui.collectAsState()
    val item       = ui.selectedResult
    val agentName  by authVm.userName.collectAsState(initial = "")
    val agentPhone by authVm.userMobile.collectAsState(initial = "")
    val context    = LocalContext.current

    LaunchedEffect(item) {
        if (item == null) return@LaunchedEffect
        val userId = authVm.userId.first()
        if (userId == 0L) return@LaunchedEffect
        val loc     = getLocationOnce(context)
        val address = reverseGeocode(context, loc?.latitude, loc?.longitude)
        runCatching {
            ApiClient.api.logSearch(
                SearchLogRequest(
                    userId        = userId,
                    vehicleNo     = item.vehicleNo,
                    chassisNo     = item.chassisNo,
                    model         = item.model,
                    lat           = loc?.latitude,
                    lng           = loc?.longitude,
                    address       = address,
                    deviceTimeIso = java.time.Instant.now().toString()
                )
            )
        }
    }

    Scaffold(
        topBar = {
            TopAppBar(
                title = { Text(item?.vehicleNo ?: "Vehicle Detail", fontWeight = FontWeight.Bold) },
                navigationIcon = {
                    IconButton(onClick = { nav.popBackStack() }) {
                        Icon(Icons.Default.ArrowBack, null)
                    }
                }
            )
        },
        floatingActionButton = {
            if (item != null) {
                ExtendedFloatingActionButton(
                    onClick = { nav.navigate(Screen.Confirm.route) },
                    icon = { Icon(Icons.Default.KeyboardArrowDown, null) },
                    text = { Text("Send Confirm", fontWeight = FontWeight.Bold) },
                    containerColor = MaterialTheme.colorScheme.primary,
                    contentColor   = MaterialTheme.colorScheme.onPrimary
                )
            }
        }
    ) { pad ->
        if (item == null) {
            Box(Modifier.fillMaxSize().padding(pad), contentAlignment = Alignment.Center) {
                Text("No vehicle selected.", color = MaterialTheme.colorScheme.onSurfaceVariant)
            }
            return@Scaffold
        }

        Column(
            Modifier
                .padding(pad)
                .fillMaxSize()
                .verticalScroll(rememberScrollState())
                .padding(16.dp),
            verticalArrangement = Arrangement.spacedBy(12.dp)
        ) {
            // Vehicle info card
            Card(
                shape = RoundedCornerShape(14.dp),
                colors = CardDefaults.cardColors(
                    containerColor = MaterialTheme.colorScheme.surface),
                elevation = CardDefaults.cardElevation(2.dp),
                modifier = Modifier.fillMaxWidth()
            ) {
                Column(Modifier.padding(18.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
                    Text("Vehicle Information",
                        style = MaterialTheme.typography.titleSmall,
                        fontWeight = FontWeight.Bold,
                        color = MaterialTheme.colorScheme.primary)
                    HorizontalDivider(color = MaterialTheme.colorScheme.primary.copy(alpha = 0.3f))

                    DetailRow("Vehicle No",   item.vehicleNo,
                        invalid = item.vehicleNo.isNotBlank() && !item.vehicleNo.isValidRc())
                    DetailRow("Chassis No",   item.chassisNo)
                    DetailRow("Engine No",    item.engineNo)
                    DetailRow("Model / Make", item.model)
                    DetailRow("Customer",     item.customerName)
                }
            }

            // Agency card
            Card(
                shape = RoundedCornerShape(14.dp),
                colors = CardDefaults.cardColors(
                    containerColor = MaterialTheme.colorScheme.primaryContainer),
                modifier = Modifier.fillMaxWidth()
            ) {
                Column(Modifier.padding(18.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                    Text("Agency",
                        style = MaterialTheme.typography.titleSmall,
                        fontWeight = FontWeight.Bold,
                        color = MaterialTheme.colorScheme.onPrimaryContainer)
                    HorizontalDivider(color = MaterialTheme.colorScheme.onPrimaryContainer.copy(alpha = 0.2f))
                    DetailRow("Name",   "V K Enterprises",
                        valueColor = MaterialTheme.colorScheme.onPrimaryContainer)
                    if (agentName.isNotBlank())
                        DetailRow("Agent",  agentName,
                            valueColor = MaterialTheme.colorScheme.onPrimaryContainer)
                    if (agentPhone.isNotBlank())
                        DetailRow("Mobile", agentPhone,
                            valueColor = MaterialTheme.colorScheme.onPrimaryContainer)
                }
            }

            Spacer(Modifier.height(72.dp)) // space for FAB
        }
    }
}

private suspend fun reverseGeocode(context: Context, lat: Double?, lng: Double?): String? {
    if (lat == null || lng == null) return null
    return withContext(Dispatchers.IO) {
        runCatching {
            val geocoder = android.location.Geocoder(context, java.util.Locale.getDefault())
            @Suppress("DEPRECATION")
            val addresses = geocoder.getFromLocation(lat, lng, 1)
            addresses?.firstOrNull()?.let { addr ->
                val line0 = addr.getAddressLine(0)
                if (!line0.isNullOrBlank()) line0
                else listOfNotNull(
                    addr.subLocality ?: addr.locality,
                    addr.subAdminArea ?: addr.adminArea,
                    addr.countryName
                ).filter { it.isNotBlank() }.joinToString(", ")
            }
        }.getOrNull()
    }
}

@SuppressLint("MissingPermission")
private suspend fun getLocationOnce(context: Context): android.location.Location? =
    suspendCancellableCoroutine { cont ->
        val fused = LocationServices.getFusedLocationProviderClient(context)
        fused.getCurrentLocation(Priority.PRIORITY_HIGH_ACCURACY, null)
            .addOnSuccessListener { loc ->
                if (loc != null) { cont.resume(loc); return@addOnSuccessListener }
                fused.lastLocation
                    .addOnSuccessListener { last -> cont.resume(last) }
                    .addOnFailureListener { cont.resume(null) }
            }
            .addOnFailureListener {
                fused.lastLocation
                    .addOnSuccessListener { last -> cont.resume(last) }
                    .addOnFailureListener { cont.resume(null) }
            }
    }

@Composable
private fun DetailRow(
    label: String,
    value: String,
    valueColor: androidx.compose.ui.graphics.Color = MaterialTheme.colorScheme.onSurface,
    invalid: Boolean = false
) {
    if (value.isBlank()) return
    Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween,
        verticalAlignment = Alignment.CenterVertically) {
        Text(label,
            style = MaterialTheme.typography.bodySmall,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
            modifier = Modifier.weight(0.38f))
        Row(Modifier.weight(0.62f), verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(6.dp)) {
            Text(value,
                style = MaterialTheme.typography.bodySmall,
                fontWeight = FontWeight.SemiBold,
                fontFamily = if (label == "Vehicle No" || label == "Chassis No" || label == "Engine No")
                    FontFamily.Monospace else FontFamily.Default,
                color = if (invalid) MaterialTheme.colorScheme.error else valueColor)
            if (invalid) {
                Surface(
                    shape = androidx.compose.foundation.shape.RoundedCornerShape(4.dp),
                    color = MaterialTheme.colorScheme.errorContainer
                ) {
                    Text("INVALID",
                        style = MaterialTheme.typography.labelSmall,
                        color = MaterialTheme.colorScheme.onErrorContainer,
                        modifier = Modifier.padding(horizontal = 5.dp, vertical = 2.dp))
                }
            }
        }
    }
}

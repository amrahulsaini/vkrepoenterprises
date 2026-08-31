package com.vkenterprises.crmrs.ui.screens

import android.content.Intent
import android.net.Uri
import androidx.compose.animation.animateColorAsState
import androidx.compose.animation.core.*
import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.Image
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.*
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.*
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.*
import androidx.navigation.NavController
import okhttp3.MediaType.Companion.toMediaTypeOrNull
import okhttp3.RequestBody.Companion.toRequestBody
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import coil.compose.AsyncImage
import com.vkenterprises.crmrs.BuildConfig
import com.vkenterprises.crmrs.R
import com.vkenterprises.crmrs.navigation.Screen
import com.vkenterprises.crmrs.viewmodel.AuthViewModel
import com.vkenterprises.crmrs.viewmodel.SearchViewModel
import com.vkenterprises.crmrs.viewmodel.SettingsViewModel
import com.vkenterprises.crmrs.ui.theme.RobotoFamily

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun SettingsScreen(
    settingsVm: SettingsViewModel,
    searchVm: SearchViewModel,
    authVm: AuthViewModel,
    nav: NavController
) {
    val ui       by settingsVm.ui.collectAsState()
    val searchUi by searchVm.ui.collectAsState()
    val isAdmin  by authVm.isAdmin.collectAsState(initial = false)

    Scaffold(
        topBar = {
            TopAppBar(
                title = { Text("Settings", fontWeight = FontWeight.Bold) },
                navigationIcon = {
                    IconButton(onClick = { nav.popBackStack() }) {
                        Icon(Icons.Default.ArrowBack, contentDescription = "Back")
                    }
                },
                colors = TopAppBarDefaults.topAppBarColors(
                    containerColor = MaterialTheme.colorScheme.surface
                )
            )
        }
    ) { pad ->
        LazyColumn(
            modifier = Modifier.padding(pad).fillMaxSize(),
            contentPadding = PaddingValues(16.dp),
            verticalArrangement = Arrangement.spacedBy(12.dp)
        ) {

            if (isAdmin) {
                item {
                    androidx.compose.material3.ListItem(
                        headlineContent = { Text("Fingerprint sign-in") },
                        supportingContent = { Text("Use your fingerprint to sign in on the CRMRS desktop") },
                        leadingContent = {
                            Icon(androidx.compose.material.icons.Icons.Filled.Fingerprint, contentDescription = null)
                        },
                        modifier = Modifier.clickable {
                            nav.navigate(com.vkenterprises.crmrs.navigation.Screen.Fingerprint.route)
                        }
                    )
                }
                item {
                    androidx.compose.material3.ListItem(
                        headlineContent = { Text("Scan desktop code") },
                        supportingContent = { Text("Approve a CRMRS desktop sign-in") },
                        leadingContent = {
                            Icon(androidx.compose.material.icons.Icons.Filled.QrCodeScanner, contentDescription = null)
                        },
                        modifier = Modifier.clickable {
                            nav.navigate(com.vkenterprises.crmrs.navigation.Screen.FingerprintScan.route)
                        }
                    )
                }

                item {
                    val ctx = LocalContext.current
                    val scope = rememberCoroutineScope()
                    var lhBusy by remember { mutableStateOf(false) }
                    var lhMsg  by remember { mutableStateOf<String?>(null) }
                    val picker = androidx.activity.compose.rememberLauncherForActivityResult(
                        androidx.activity.result.contract.ActivityResultContracts.GetContent()
                    ) { uri ->
                        if (uri == null) return@rememberLauncherForActivityResult
                        lhBusy = true; lhMsg = null
                        scope.launch {
                            val ok = runCatching {
                                val bytes = withContext(kotlinx.coroutines.Dispatchers.IO) {
                                    ctx.contentResolver.openInputStream(uri)?.use { it.readBytes() }
                                } ?: return@runCatching false
                                val type = ctx.contentResolver.getType(uri) ?: "image/png"
                                val ext  = if (type.contains("jp")) "jpg" else "png"
                                val body = bytes.toRequestBody(type.toMediaTypeOrNull())
                                val part = okhttp3.MultipartBody.Part.createFormData(
                                    "file", "letterhead.$ext", body
                                )
                                com.vkenterprises.crmrs.data.api.ApiClient.api
                                    .uploadLetterhead(part).isSuccessful
                            }.getOrDefault(false)
                            lhMsg = if (ok) "Letterhead uploaded." else "Upload failed. Try a PNG or JPG under 8 MB."
                            lhBusy = false
                        }
                    }
                    SectionCard(title = "Letterhead") {
                        Text(
                            "Used on the Authority Letter. Upload your letterhead with the agency name and signature.",
                            style = MaterialTheme.typography.bodySmall,
                            color = MaterialTheme.colorScheme.onSurfaceVariant,
                            modifier = Modifier.padding(bottom = 8.dp)
                        )
                        Button(
                            onClick = { picker.launch("image/*") },
                            enabled = !lhBusy,
                            modifier = Modifier.fillMaxWidth()
                        ) {
                            Icon(Icons.Default.Upload, null, Modifier.size(18.dp))
                            Spacer(Modifier.width(6.dp))
                            Text(if (lhBusy) "Uploading…" else "Upload Letterhead")
                        }
                        lhMsg?.let {
                            Spacer(Modifier.height(6.dp))
                            Text(it, style = MaterialTheme.typography.labelSmall,
                                color = MaterialTheme.colorScheme.onSurfaceVariant)
                        }
                    }
                }

                item {
                    SectionCard(title = "Admin Tools") {
                        Button(
                            onClick = { nav.navigate(Screen.LiveUsers.route) },
                            modifier = Modifier.fillMaxWidth(),
                            colors = ButtonDefaults.buttonColors(
                                containerColor = Color(0xFF1A237E),
                                contentColor   = Color.White
                            )
                        ) {
                            Icon(Icons.Default.LocationOn, null, Modifier.size(18.dp))
                            Spacer(Modifier.width(8.dp))
                            Text("View Live Users", fontWeight = FontWeight.SemiBold)
                        }
                    }
                }
            }


            item {
                SectionCard(title = "Display") {
                    Row(
                        Modifier.fillMaxWidth().padding(vertical = 4.dp),
                        horizontalArrangement = Arrangement.SpaceBetween,
                        verticalAlignment = Alignment.CenterVertically
                    ) {
                        Column(Modifier.weight(1f)) {
                            Text("Two-Column Grid", style = MaterialTheme.typography.bodyMedium,
                                fontWeight = FontWeight.Medium)
                            Text("Show results in 2-column grid vs single list",
                                style = MaterialTheme.typography.bodySmall,
                                color = MaterialTheme.colorScheme.onSurfaceVariant)
                        }
                        Switch(
                            checked = searchUi.twoColumnView,
                            onCheckedChange = { searchVm.setTwoColumnView(it) }
                        )
                    }
                    HorizontalDivider(modifier = Modifier.padding(vertical = 4.dp))
                    Row(
                        Modifier.fillMaxWidth().padding(vertical = 4.dp),
                        horizontalArrangement = Arrangement.SpaceBetween,
                        verticalAlignment = Alignment.CenterVertically
                    ) {
                        Column(Modifier.weight(1f)) {
                            Text("View Only Online", style = MaterialTheme.typography.bodyMedium,
                                fontWeight = FontWeight.Medium)
                            Text("Latest results",
                                style = MaterialTheme.typography.bodySmall,
                                color = MaterialTheme.colorScheme.onSurfaceVariant)
                        }
                        Switch(
                            checked = searchUi.onlineOnly,
                            onCheckedChange = { searchVm.setOnlineOnly(it) }
                        )
                    }
                    HorizontalDivider(modifier = Modifier.padding(vertical = 4.dp))
                    Row(
                        Modifier.fillMaxWidth().padding(vertical = 4.dp),
                        horizontalArrangement = Arrangement.SpaceBetween,
                        verticalAlignment = Alignment.CenterVertically
                    ) {
                        Column(Modifier.weight(1f)) {
                            Text("Hyphen Vehicles", style = MaterialTheme.typography.bodyMedium,
                                fontWeight = FontWeight.Medium)
                            Text("Show RC numbers with hyphens; turn off to hide them",
                                style = MaterialTheme.typography.bodySmall,
                                color = MaterialTheme.colorScheme.onSurfaceVariant)
                        }
                        Switch(
                            checked = searchUi.showHyphens,
                            onCheckedChange = { searchVm.setShowHyphens(it) }
                        )
                    }
                }
            }

            item {
                SectionCard(title = "Account") {
                    OutlinedButton(
                        onClick = { nav.navigate(Screen.Profile.route) },
                        modifier = Modifier.fillMaxWidth()
                    ) {
                        Icon(Icons.Default.AccountCircle, null, Modifier.size(18.dp))
                        Spacer(Modifier.width(6.dp))
                        Text("My Account")
                    }
                }
            }

            item {
                val agencyName by authVm.agencyName.collectAsState(initial = null)
                val agencyLogo by authVm.agencyLogo.collectAsState(initial = null)
                val logoUrl = agencyLogo?.takeIf { it.isNotBlank() }
                    ?.let { BuildConfig.BASE_URL.trimEnd('/') + "/" + it.trimStart('/') }
                val ctx = LocalContext.current
                fun openSite() = runCatching {
                    ctx.startActivity(Intent(Intent.ACTION_VIEW, Uri.parse("https://crmrecoverysoftware.com/")))
                }

                Column(
                    Modifier.fillMaxWidth().padding(top = 24.dp, bottom = 12.dp),
                    horizontalAlignment = Alignment.CenterHorizontally,
                    verticalArrangement = Arrangement.spacedBy(6.dp)
                ) {
                    Surface(
                        shape  = RoundedCornerShape(14.dp),
                        color  = Color.White,
                        border = BorderStroke(1.dp, MaterialTheme.colorScheme.outlineVariant),
                        modifier = Modifier.size(72.dp)
                    ) {
                        if (logoUrl != null) {
                            AsyncImage(
                                model = logoUrl, contentDescription = agencyName,
                                contentScale = ContentScale.Fit,
                                modifier = Modifier.fillMaxSize().padding(6.dp)
                            )
                        } else {
                            Image(
                                painter = painterResource(id = R.drawable.agency_logo),
                                contentDescription = agencyName,
                                contentScale = ContentScale.Fit,
                                modifier = Modifier.fillMaxSize().padding(6.dp)
                            )
                        }
                    }
                    if (!agencyName.isNullOrBlank())
                        Text(agencyName!!,
                            style = MaterialTheme.typography.titleSmall,
                            fontWeight = FontWeight.SemiBold,
                            textAlign = TextAlign.Center)

                    Spacer(Modifier.height(10.dp))
                    HorizontalDivider(Modifier.fillMaxWidth(0.5f))
                    Spacer(Modifier.height(10.dp))

                    Image(
                        painter = painterResource(id = R.drawable.crmrs_logo),
                        contentDescription = "CRMRS",
                        contentScale = ContentScale.Fit,
                        modifier = Modifier.height(36.dp).clickable { openSite() }
                    )
                    Text("Developed by CRMRS",
                        style = MaterialTheme.typography.bodySmall,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                        textAlign = TextAlign.Center)
                    Text("Version ${BuildConfig.VERSION_NAME} (${BuildConfig.VERSION_CODE})",
                        style = MaterialTheme.typography.labelSmall,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                        textAlign = TextAlign.Center)
                    Text("crmrecoverysoftware.com",
                        style = MaterialTheme.typography.bodySmall,
                        color = MaterialTheme.colorScheme.primary,
                        textAlign = TextAlign.Center,
                        modifier = Modifier.clickable { openSite() })
                    Text("rahul@loopwar.dev",
                        style = MaterialTheme.typography.labelSmall,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                        textAlign = TextAlign.Center)
                    Text(SUPPORT_CONTACT,
                        style = MaterialTheme.typography.bodySmall,
                        color = MaterialTheme.colorScheme.primary,
                        textAlign = TextAlign.Center,
                        modifier = Modifier.clickable {
                            runCatching {
                                ctx.startActivity(
                                    Intent(Intent.ACTION_DIAL, Uri.parse("tel:$SUPPORT_CONTACT"))
                                )
                            }
                        })
                }
            }

            item { Spacer(Modifier.height(16.dp)) }
        }
    }
}

@Composable
private fun SectionCard(title: String, content: @Composable ColumnScope.() -> Unit) {
    Card(modifier = Modifier.fillMaxWidth()) {
        Column(Modifier.padding(16.dp)) {
            Text(
                title,
                style = MaterialTheme.typography.titleSmall,
                fontWeight = FontWeight.Bold,
                color = MaterialTheme.colorScheme.primary,
                modifier = Modifier.padding(bottom = 10.dp)
            )
            content()
        }
    }
}

@Composable
private fun StatRow(label: String, value: Long) {
    Row(
        Modifier.fillMaxWidth().padding(vertical = 4.dp),
        horizontalArrangement = Arrangement.SpaceBetween
    ) {
        Text(label, style = MaterialTheme.typography.bodySmall,
            color = MaterialTheme.colorScheme.onSurfaceVariant)
        Text(
            value.formatStatCount(),
            style = MaterialTheme.typography.bodySmall,
            fontWeight = FontWeight.SemiBold,
            fontFamily = RobotoFamily
        )
    }
}

private fun Long.formatStatCount(): String = when {
    this >= 1_000_000L -> "${this / 1_000_000}.${(this % 1_000_000L) / 100_000L}M"
    this >= 1_000L     -> "${this / 1_000}.${(this % 1_000L) / 100L}K"
    else               -> "$this"
}

package com.vkenterprises.crmrs.ui.screens

import androidx.compose.foundation.layout.*
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Fingerprint
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.fragment.app.FragmentActivity
import com.vkenterprises.crmrs.data.api.ApiService
import com.vkenterprises.crmrs.security.BiometricGate
import com.vkenterprises.crmrs.security.FingerprintKey
import kotlinx.coroutines.launch

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun FingerprintScreen(
    api: ApiService,
    onBack: () -> Unit
) {
    val ctx = LocalContext.current
    val activity = ctx as? FragmentActivity
    val scope = rememberCoroutineScope()

    var loading by remember { mutableStateOf(true) }
    var enrolled by remember { mutableStateOf(false) }
    var keyId by remember { mutableStateOf("") }
    var device by remember { mutableStateOf("") }
    var enrolledAt by remember { mutableStateOf("") }
    var msg by remember { mutableStateOf<String?>(null) }
    var busy by remember { mutableStateOf(false) }

    suspend fun refresh() {
        loading = true
        runCatching { api.fpStatus() }.getOrNull()?.body()?.let { b ->
            enrolled = (b["enrolled"] as? Boolean) == true && FingerprintKey.exists()
            keyId = b["keyId"] as? String ?: ""
            device = b["device"] as? String ?: ""
            enrolledAt = b["enrolledAt"] as? String ?: ""
        }
        loading = false
    }

    LaunchedEffect(Unit) { refresh() }

    Scaffold(
        topBar = {
            TopAppBar(
                title = { Text("Fingerprint sign-in") },
                navigationIcon = { TextButton(onClick = onBack) { Text("Back") } }
            )
        }
    ) { pad ->
        Column(
            Modifier
                .padding(pad)
                .padding(20.dp)
                .fillMaxSize()
                .verticalScroll(rememberScrollState()),
            horizontalAlignment = Alignment.CenterHorizontally
        ) {
            Icon(
                Icons.Filled.Fingerprint,
                contentDescription = null,
                modifier = Modifier.size(72.dp),
                tint = MaterialTheme.colorScheme.primary
            )
            Spacer(Modifier.height(14.dp))

            Text(
                if (enrolled) "Fingerprint is set up" else "Set up fingerprint sign-in",
                style = MaterialTheme.typography.titleLarge,
                fontWeight = FontWeight.Bold
            )
            Spacer(Modifier.height(6.dp))
            Text(
                if (enrolled)
                    "Scan the QR on the CRMRS desktop and confirm with your fingerprint to sign in."
                else
                    "Your fingerprint stays on this phone. CRMRS never receives or stores it.",
                style = MaterialTheme.typography.bodyMedium,
                color = MaterialTheme.colorScheme.onSurfaceVariant
            )

            if (enrolled) {
                Spacer(Modifier.height(18.dp))
                Card(Modifier.fillMaxWidth()) {
                    Column(Modifier.padding(14.dp)) {
                        Row(Modifier.fillMaxWidth()) {
                            Text("Key ID", Modifier.weight(1f), style = MaterialTheme.typography.labelMedium)
                            Text(keyId, style = MaterialTheme.typography.bodySmall)
                        }
                        if (device.isNotBlank()) {
                            Spacer(Modifier.height(6.dp))
                            Row(Modifier.fillMaxWidth()) {
                                Text("Device", Modifier.weight(1f), style = MaterialTheme.typography.labelMedium)
                                Text(device, style = MaterialTheme.typography.bodySmall)
                            }
                        }
                        if (enrolledAt.isNotBlank()) {
                            Spacer(Modifier.height(6.dp))
                            Row(Modifier.fillMaxWidth()) {
                                Text("Set up", Modifier.weight(1f), style = MaterialTheme.typography.labelMedium)
                                Text(enrolledAt, style = MaterialTheme.typography.bodySmall)
                            }
                        }
                    }
                }
            }

            msg?.let {
                Spacer(Modifier.height(14.dp))
                Text(it, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.error)
            }

            Spacer(Modifier.height(22.dp))

            if (loading) {
                CircularProgressIndicator()
            } else {
                Button(
                    onClick = {
                        if (activity == null) { msg = "Cannot show the fingerprint prompt here."; return@Button }
                        when (val r = BiometricGate.ready(activity)) {
                            is BiometricGate.Ready.NoHardware ->
                                msg = "This phone has no fingerprint sensor."
                            is BiometricGate.Ready.NotEnrolled ->
                                msg = "Add a fingerprint in your phone's settings first."
                            is BiometricGate.Ready.Unavailable -> msg = r.reason
                            is BiometricGate.Ready.Yes -> {
                                busy = true; msg = null
                                scope.launch {
                                    try {
                                        val pub = FingerprintKey.create()
                                        val sig = FingerprintKey.signatureForPrompt()
                                        val res = BiometricGate.authenticate(
                                            activity,
                                            "Confirm your fingerprint",
                                            "This links your fingerprint to CRMRS",
                                            sig
                                        )
                                        if (!res.ok) {
                                            FingerprintKey.delete()
                                            msg = res.error ?: "Cancelled."
                                        } else {
                                            val resp = api.fpEnrol(
                                                mapOf(
                                                    "publicKey" to pub,
                                                    "deviceLabel" to (android.os.Build.MANUFACTURER + " " + android.os.Build.MODEL)
                                                )
                                            )
                                            if (resp.isSuccessful) { msg = null; refresh() }
                                            else { FingerprintKey.delete(); msg = "Could not save: " + resp.code() }
                                        }
                                    } catch (e: Exception) {
                                        FingerprintKey.delete()
                                        msg = e.message ?: "Setup failed."
                                    } finally { busy = false }
                                }
                            }
                        }
                    },
                    enabled = !busy,
                    modifier = Modifier.fillMaxWidth()
                ) { Text(if (enrolled) "Set up again on this phone" else "Set up fingerprint") }

                if (enrolled) {
                    Spacer(Modifier.height(10.dp))
                    OutlinedButton(
                        onClick = {
                            busy = true
                            scope.launch {
                                runCatching { api.fpRemove() }
                                FingerprintKey.delete()
                                busy = false
                                refresh()
                            }
                        },
                        enabled = !busy,
                        modifier = Modifier.fillMaxWidth()
                    ) { Text("Remove from this phone") }
                }
            }
        }
    }
}

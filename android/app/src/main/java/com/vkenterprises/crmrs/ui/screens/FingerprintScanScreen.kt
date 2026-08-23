package com.vkenterprises.crmrs.ui.screens

import android.Manifest
import android.content.pm.PackageManager
import android.util.Size
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.camera.core.CameraSelector
import androidx.camera.core.ImageAnalysis
import androidx.camera.core.ImageProxy
import androidx.camera.core.Preview
import androidx.camera.lifecycle.ProcessCameraProvider
import androidx.camera.view.PreviewView
import androidx.compose.foundation.layout.*
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.LocalLifecycleOwner
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.viewinterop.AndroidView
import androidx.core.content.ContextCompat
import androidx.fragment.app.FragmentActivity
import com.google.mlkit.vision.barcode.BarcodeScanning
import com.google.mlkit.vision.barcode.common.Barcode
import com.google.mlkit.vision.common.InputImage
import com.vkenterprises.crmrs.data.api.ApiService
import com.vkenterprises.crmrs.security.BiometricGate
import com.vkenterprises.crmrs.security.FingerprintKey
import kotlinx.coroutines.launch
import java.util.concurrent.Executors

private fun serverMessage(r: retrofit2.Response<*>): String? {
    return try {
        val body = r.errorBody()?.string() ?: return null
        val m = Regex("\"message\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"").find(body) ?: return null
        m.groupValues[1].replace("\\\"", "\"").replace("\\\\", "\\").ifBlank { null }
    } catch (e: Exception) { null }
}

private fun challengeIdFrom(raw: String?): String? {
    if (raw.isNullOrBlank()) return null
    val m = Regex("[?&]c=([0-9a-fA-F]{32})").find(raw)
    if (m != null) return m.groupValues[1]
    return if (Regex("^[0-9a-fA-F]{32}$").matches(raw.trim())) raw.trim() else null
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun FingerprintScanScreen(api: ApiService, onDone: () -> Unit) {
    val ctx = LocalContext.current
    val activity = ctx as? FragmentActivity
    val lifecycleOwner = LocalLifecycleOwner.current
    val scope = rememberCoroutineScope()

    var granted by remember {
        mutableStateOf(
            ContextCompat.checkSelfPermission(ctx, Manifest.permission.CAMERA) ==
                PackageManager.PERMISSION_GRANTED
        )
    }
    val askCamera = rememberLauncherForActivityResult(
        ActivityResultContracts.RequestPermission()
    ) { granted = it }

    LaunchedEffect(Unit) { if (!granted) askCamera.launch(Manifest.permission.CAMERA) }

    var handled by remember { mutableStateOf(false) }
    var status by remember { mutableStateOf("Point the camera at the code on the desktop.") }
    var pairCode by remember { mutableStateOf("") }
    var mode by remember { mutableStateOf("") }
    var device by remember { mutableStateOf("") }
    var nonce by remember { mutableStateOf("") }
    var challenge by remember { mutableStateOf("") }
    var confirming by remember { mutableStateOf(false) }
    var busy by remember { mutableStateOf(false) }
    var forMobile by remember { mutableStateOf("") }

    fun onCode(raw: String?) {
        if (handled) return
        val id = challengeIdFrom(raw) ?: return
        handled = true
        status = "Reading the request..."
        scope.launch {
            try {
                val r = api.fpChallenge(id)
                val b = r.body()
                if (!r.isSuccessful || b == null) {
                    status = when (r.code()) {
                        410 -> "That code has expired. Get a new one on the desktop."
                        409 -> "That code has already been used."
                        403 -> serverMessage(r) ?: "That code belongs to a different agency."
                        else -> "That code is not valid."
                    }
                    handled = false
                    return@launch
                }
                challenge = id
                nonce = b["nonce"] as? String ?: ""
                pairCode = b["pairCode"] as? String ?: ""
                mode = b["mode"] as? String ?: ""
                device = b["deviceLabel"] as? String ?: ""
                forMobile = b["forMobile"] as? String ?: ""
                confirming = true
            } catch (e: Exception) {
                status = e.message ?: "Could not read that code."
                handled = false
            }
        }
    }

    if (confirming) {
        AlertDialog(
            onDismissRequest = { },
            title = { Text("Approve sign-in?") },
            text = {
                Column {
                    Text(
                        buildString {
                            append("Open ")
                            append(if (mode.isBlank()) "CRMRS" else mode)
                            if (device.isNotBlank()) { append(" on "); append(device) }
                            append(" ?")
                        }
                    )
                    if (forMobile.isNotBlank()) {
                        Spacer(Modifier.height(8.dp))
                        Text(
                            "Started for " + forMobile + ".",
                            style = MaterialTheme.typography.bodySmall,
                            color = MaterialTheme.colorScheme.onSurfaceVariant
                        )
                    }
                    Spacer(Modifier.height(14.dp))
                    Text("CODE", style = MaterialTheme.typography.labelSmall)
                    Text(
                        pairCode,
                        style = MaterialTheme.typography.headlineMedium,
                        fontWeight = FontWeight.Bold,
                        color = MaterialTheme.colorScheme.primary
                    )
                    Spacer(Modifier.height(8.dp))
                    Text(
                        "Only continue if this matches the code shown on the desktop.",
                        style = MaterialTheme.typography.bodySmall,
                        color = MaterialTheme.colorScheme.onSurfaceVariant
                    )
                }
            },
            confirmButton = {
                TextButton(
                    enabled = !busy,
                    onClick = {
                        if (activity == null) return@TextButton
                        busy = true
                        scope.launch {
                            try {
                                if (!FingerprintKey.exists()) {
                                    status = "Set up fingerprint on this phone first."
                                    confirming = false; handled = false; busy = false
                                    return@launch
                                }
                                val sig = FingerprintKey.signatureForPrompt()
                                val res = BiometricGate.authenticate(
                                    activity,
                                    "Confirm sign-in",
                                    if (mode.isBlank()) "CRMRS desktop" else mode,
                                    sig
                                )
                                val signed = res.signature
                                if (!res.ok || signed == null) {
                                    status = res.error ?: "Cancelled."
                                    confirming = false; handled = false; busy = false
                                    return@launch
                                }
                                val payload = FingerprintKey.finish(signed, "$challenge:$nonce")
                                val ok = api.fpApprove(
                                    mapOf("challengeId" to challenge, "signature" to payload)
                                )
                                if (ok.isSuccessful) {
                                    status = "Approved. The desktop is opening."
                                    confirming = false
                                    onDone()
                                } else {
                                    status = serverMessage(ok)
                                        ?: ("Could not approve (" + ok.code() + ").")
                                    confirming = false; handled = false
                                }
                            } catch (e: Exception) {
                                status = e.message ?: "Approval failed."
                                confirming = false; handled = false
                            } finally { busy = false }
                        }
                    }
                ) { Text(if (busy) "Working..." else "Use fingerprint") }
            },
            dismissButton = {
                TextButton(enabled = !busy, onClick = {
                    confirming = false; handled = false
                    status = "Cancelled. Point the camera at the code."
                }) { Text("Cancel") }
            }
        )
    }

    Scaffold(
        topBar = {
            TopAppBar(
                title = { Text("Scan desktop code") },
                navigationIcon = { TextButton(onClick = onDone) { Text("Back") } }
            )
        }
    ) { pad ->
        Column(
            Modifier.padding(pad).fillMaxSize(),
            horizontalAlignment = Alignment.CenterHorizontally
        ) {
            if (granted) {
                AndroidView(
                    modifier = Modifier.fillMaxWidth().weight(1f),
                    factory = { c ->
                        val view = PreviewView(c)
                        val exec = Executors.newSingleThreadExecutor()
                        val future = ProcessCameraProvider.getInstance(c)
                        future.addListener({
                            val provider = future.get()
                            val preview = Preview.Builder().build().also {
                                it.setSurfaceProvider(view.surfaceProvider)
                            }
                            val scanner = BarcodeScanning.getClient()
                            val analysis = ImageAnalysis.Builder()
                                .setTargetResolution(Size(1280, 720))
                                .setBackpressureStrategy(ImageAnalysis.STRATEGY_KEEP_ONLY_LATEST)
                                .build()
                            analysis.setAnalyzer(exec) { proxy: ImageProxy ->
                                val media = proxy.image
                                if (media == null) { proxy.close(); return@setAnalyzer }
                                val img = InputImage.fromMediaImage(media, proxy.imageInfo.rotationDegrees)
                                scanner.process(img)
                                    .addOnSuccessListener { codes ->
                                        codes.firstOrNull { it.valueType == Barcode.TYPE_TEXT || it.rawValue != null }
                                            ?.rawValue?.let { onCode(it) }
                                    }
                                    .addOnCompleteListener { proxy.close() }
                            }
                            provider.unbindAll()
                            provider.bindToLifecycle(
                                lifecycleOwner, CameraSelector.DEFAULT_BACK_CAMERA, preview, analysis
                            )
                        }, ContextCompat.getMainExecutor(c))
                        view
                    }
                )
            } else {
                Box(Modifier.fillMaxWidth().weight(1f), contentAlignment = Alignment.Center) {
                    Button(onClick = { askCamera.launch(Manifest.permission.CAMERA) }) {
                        Text("Allow camera")
                    }
                }
            }

            Text(
                status,
                Modifier.padding(18.dp),
                style = MaterialTheme.typography.bodyMedium,
                color = MaterialTheme.colorScheme.onSurfaceVariant
            )
        }
    }
}

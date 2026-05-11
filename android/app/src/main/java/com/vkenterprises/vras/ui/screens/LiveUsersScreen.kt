package com.vkenterprises.vras.ui.screens

import android.annotation.SuppressLint
import android.webkit.WebSettings
import android.webkit.WebView
import android.webkit.WebViewClient
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.ArrowBack
import androidx.compose.material.icons.filled.LocationOn
import androidx.compose.material.icons.filled.LocationOff
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.compose.ui.viewinterop.AndroidView
import androidx.navigation.NavController
import com.vkenterprises.vras.data.api.ApiClient
import com.vkenterprises.vras.data.models.LiveUser
import kotlinx.coroutines.delay
import org.json.JSONArray
import org.json.JSONObject

@OptIn(ExperimentalMaterial3Api::class)
@SuppressLint("SetJavaScriptEnabled")
@Composable
fun LiveUsersScreen(
    userId: Long,
    navController: NavController
) {
    var users     by remember { mutableStateOf<List<LiveUser>>(emptyList()) }
    var loading   by remember { mutableStateOf(true) }
    var error     by remember { mutableStateOf<String?>(null) }
    var webView   by remember { mutableStateOf<WebView?>(null) }
    var mapReady  by remember { mutableStateOf(false) }

    fun buildMarkersJson(list: List<LiveUser>): String {
        val arr = JSONArray()
        list.forEach { u ->
            val obj = JSONObject()
            obj.put("name", u.name)
            obj.put("mobile", u.mobile)
            obj.put("lastSeen", u.lastSeen)
            if (u.lat != null) obj.put("lat", u.lat) else obj.put("lat", JSONObject.NULL)
            if (u.lng != null) obj.put("lng", u.lng) else obj.put("lng", JSONObject.NULL)
            arr.put(obj)
        }
        return arr.toString()
    }

    fun pushMarkersToMap(list: List<LiveUser>) {
        val wv = webView ?: return
        if (!mapReady) return
        val json = buildMarkersJson(list)
        val escaped = json.replace("\\", "\\\\").replace("'", "\\'")
        wv.evaluateJavascript("updateMarkers('$escaped')", null)
    }

    LaunchedEffect(Unit) {
        while (true) {
            runCatching {
                val resp = ApiClient.api.getLiveUsers(userId)
                if (resp.isSuccessful) {
                    val body = resp.body()
                    if (body != null) {
                        users = body.users
                        error = null
                        pushMarkersToMap(users)
                    }
                } else {
                    error = "Failed to load live users (${resp.code()})"
                }
            }.onFailure { e -> error = e.message }
            loading = false
            delay(30_000)
        }
    }

    LaunchedEffect(mapReady) {
        if (mapReady && users.isNotEmpty()) {
            pushMarkersToMap(users)
        }
    }

    Scaffold(
        topBar = {
            TopAppBar(
                title = {
                    Column {
                        Text("Live Users", fontWeight = FontWeight.Bold)
                        Text("Active in last 15 minutes",
                            style = MaterialTheme.typography.bodySmall,
                            color = MaterialTheme.colorScheme.onSurfaceVariant)
                    }
                },
                navigationIcon = {
                    IconButton(onClick = { navController.popBackStack() }) {
                        Icon(Icons.Default.ArrowBack, contentDescription = "Back")
                    }
                },
                actions = {
                    if (!loading) {
                        Surface(
                            color = if (users.isNotEmpty()) Color(0xFF00C853) else MaterialTheme.colorScheme.surfaceVariant,
                            shape = CircleShape,
                            modifier = Modifier.padding(end = 12.dp)
                        ) {
                            Text(
                                "${users.size} online",
                                modifier = Modifier.padding(horizontal = 10.dp, vertical = 4.dp),
                                style = MaterialTheme.typography.labelSmall,
                                fontWeight = FontWeight.Bold,
                                color = if (users.isNotEmpty()) Color.White
                                        else MaterialTheme.colorScheme.onSurfaceVariant
                            )
                        }
                    }
                }
            )
        }
    ) { pad ->
        Column(Modifier.padding(pad).fillMaxSize()) {

            // Map — takes up top half
            Box(
                Modifier
                    .fillMaxWidth()
                    .weight(1f)
            ) {
                AndroidView(
                    modifier = Modifier.fillMaxSize(),
                    factory = { ctx ->
                        WebView(ctx).also { wv ->
                            wv.settings.apply {
                                javaScriptEnabled = true
                                domStorageEnabled  = true
                                cacheMode = WebSettings.LOAD_DEFAULT
                            }
                            wv.webViewClient = object : WebViewClient() {
                                override fun onPageFinished(view: WebView, url: String) {
                                    mapReady = true
                                }
                            }
                            wv.loadUrl("file:///android_asset/map.html")
                            webView = wv
                        }
                    }
                )
                if (loading) {
                    CircularProgressIndicator(Modifier.align(Alignment.Center))
                }
            }

            // Error banner
            if (error != null) {
                Surface(color = MaterialTheme.colorScheme.errorContainer) {
                    Text(
                        error!!,
                        Modifier.fillMaxWidth().padding(10.dp),
                        style = MaterialTheme.typography.bodySmall,
                        color = MaterialTheme.colorScheme.onErrorContainer
                    )
                }
            }

            // Users list — bottom half
            if (users.isEmpty() && !loading) {
                Box(
                    Modifier.fillMaxWidth().weight(1f),
                    contentAlignment = Alignment.Center
                ) {
                    Column(horizontalAlignment = Alignment.CenterHorizontally) {
                        Icon(Icons.Default.LocationOff, null, Modifier.size(40.dp),
                            tint = MaterialTheme.colorScheme.outlineVariant)
                        Spacer(Modifier.height(8.dp))
                        Text("No users online in the last 15 minutes",
                            style = MaterialTheme.typography.bodySmall,
                            color = MaterialTheme.colorScheme.outlineVariant)
                    }
                }
            } else {
                LazyColumn(
                    Modifier.fillMaxWidth().weight(1f),
                    contentPadding = PaddingValues(bottom = 8.dp)
                ) {
                    items(users) { u ->
                        LiveUserRow(u)
                        HorizontalDivider(
                            color = MaterialTheme.colorScheme.outlineVariant.copy(alpha = 0.3f),
                            thickness = 0.5.dp
                        )
                    }
                }
            }
        }
    }
}

@Composable
private fun LiveUserRow(user: LiveUser) {
    Row(
        Modifier
            .fillMaxWidth()
            .padding(horizontal = 16.dp, vertical = 10.dp),
        verticalAlignment = Alignment.CenterVertically
    ) {
        Box(
            Modifier
                .size(8.dp)
                .clip(CircleShape)
                .background(Color(0xFF00C853))
        )
        Spacer(Modifier.width(12.dp))
        Column(Modifier.weight(1f)) {
            Text(user.name, fontWeight = FontWeight.SemiBold, fontSize = 14.sp)
            Text(user.mobile,
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant)
        }
        Column(horizontalAlignment = Alignment.End) {
            Text(user.lastSeen,
                style = MaterialTheme.typography.labelSmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant)
            if (user.lat != null && user.lng != null) {
                Icon(Icons.Default.LocationOn, null, Modifier.size(14.dp),
                    tint = Color(0xFF00C853))
            }
        }
    }
}

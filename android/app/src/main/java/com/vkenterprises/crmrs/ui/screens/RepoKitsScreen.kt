package com.vkenterprises.crmrs.ui.screens

import android.app.DownloadManager
import android.content.Context
import android.net.Uri
import android.os.Environment
import android.widget.Toast
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.*
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.navigation.NavController
import com.vkenterprises.crmrs.data.api.ApiClient
import com.vkenterprises.crmrs.data.models.HeadOfficeItem
import com.vkenterprises.crmrs.data.models.RepoKitItem
import com.vkenterprises.crmrs.viewmodel.AuthViewModel
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.launch

private val TEAL = Color(0xFF00897B)

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun RepoKitsScreen(vm: AuthViewModel, nav: NavController) {
    val context = LocalContext.current
    val scope   = rememberCoroutineScope()
    val userId by vm.userId.collectAsState(initial = -1L)

    var query    by remember { mutableStateOf("") }
    var offices  by remember { mutableStateOf<List<HeadOfficeItem>>(emptyList()) }
    var selected by remember { mutableStateOf<HeadOfficeItem?>(null) }
    var kits     by remember { mutableStateOf<List<RepoKitItem>>(emptyList()) }
    var searching by remember { mutableStateOf(false) }
    var loadingKits by remember { mutableStateOf(false) }

    fun search() {
        scope.launch {
            searching = true
            selected = null; kits = emptyList()
            runCatching {
                val uid = vm.userId.first()
                val r = ApiClient.api.searchRepoKitHeadOffices(uid, query.trim())
                if (r.isSuccessful) offices = r.body().orEmpty()
            }
            searching = false
        }
    }

    fun openOffice(o: HeadOfficeItem) {
        selected = o
        scope.launch {
            loadingKits = true
            runCatching {
                val uid = vm.userId.first()
                val r = ApiClient.api.getRepoKits(uid, o.id)
                if (r.isSuccessful) kits = r.body().orEmpty()
            }.onFailure { kits = emptyList() }
            loadingKits = false
        }
    }

    fun download(kit: RepoKitItem) {
        val url = kit.pdfUrl
        if (url.isNullOrBlank()) return
        runCatching {
            val dm = context.getSystemService(Context.DOWNLOAD_SERVICE) as DownloadManager
            val name = (kit.fileName?.takeIf { it.isNotBlank() }
                ?: "${(selected?.name ?: "repokit").replace(" ", "_")}.pdf")
            val req = DownloadManager.Request(Uri.parse(url))
                .setTitle(name)
                .setDescription("Repo kit — ${selected?.name ?: ""}")
                .setNotificationVisibility(DownloadManager.Request.VISIBILITY_VISIBLE_NOTIFY_COMPLETED)
                .setDestinationInExternalPublicDir(Environment.DIRECTORY_DOWNLOADS, name)
                .setMimeType("application/pdf")
            dm.enqueue(req)
            Toast.makeText(context, "Downloading $name…", Toast.LENGTH_SHORT).show()
        }.onFailure {
            Toast.makeText(context, "Couldn't start the download.", Toast.LENGTH_SHORT).show()
        }
    }

    Scaffold(
        topBar = {
            TopAppBar(
                title = { Text("Download Repo Kits", fontWeight = FontWeight.Bold) },
                navigationIcon = {
                    IconButton(onClick = { nav.popBackStack() }) { Icon(Icons.Default.ArrowBack, null) }
                }
            )
        }
    ) { pad ->
        Column(Modifier.padding(pad).fillMaxSize().padding(16.dp)) {
            OutlinedTextField(
                value = query,
                onValueChange = { query = it },
                label = { Text("Search head office name") },
                leadingIcon = { Icon(Icons.Default.Search, null) },
                trailingIcon = {
                    if (searching) CircularProgressIndicator(Modifier.size(20.dp), strokeWidth = 2.dp)
                    else IconButton(onClick = { search() }) { Icon(Icons.Default.ArrowForward, "Search") }
                },
                singleLine = true,
                shape = RoundedCornerShape(10.dp),
                modifier = Modifier.fillMaxWidth()
            )
            Spacer(Modifier.height(12.dp))

            if (selected == null) {
                if (offices.isEmpty() && !searching) {
                    Text("Type a head office name and search to find its repo kits.",
                        style = MaterialTheme.typography.bodySmall,
                        color = MaterialTheme.colorScheme.onSurfaceVariant)
                }
                LazyColumn(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                    items(offices, key = { it.id }) { o ->
                        Card(
                            onClick = { openOffice(o) },
                            shape = RoundedCornerShape(10.dp),
                            modifier = Modifier.fillMaxWidth()
                        ) {
                            Row(Modifier.padding(14.dp), verticalAlignment = Alignment.CenterVertically) {
                                Icon(Icons.Default.AccountBalance, null, tint = TEAL, modifier = Modifier.size(20.dp))
                                Spacer(Modifier.width(10.dp))
                                Text(o.name, style = MaterialTheme.typography.bodyMedium,
                                    fontWeight = FontWeight.Medium, modifier = Modifier.weight(1f))
                                Icon(Icons.Default.ChevronRight, null, tint = MaterialTheme.colorScheme.outline)
                            }
                        }
                    }
                }
            } else {
                // Selected head office -> its kits
                Row(verticalAlignment = Alignment.CenterVertically, modifier = Modifier.fillMaxWidth()) {
                    IconButton(onClick = { selected = null }) { Icon(Icons.Default.ArrowBack, "Back to list") }
                    Text(selected!!.name, style = MaterialTheme.typography.titleMedium,
                        fontWeight = FontWeight.Bold, modifier = Modifier.weight(1f))
                }
                Spacer(Modifier.height(8.dp))
                when {
                    loadingKits -> Box(Modifier.fillMaxWidth().padding(24.dp), contentAlignment = Alignment.Center) {
                        CircularProgressIndicator()
                    }
                    kits.isEmpty() -> Text("No repo kits uploaded for this head office yet.",
                        style = MaterialTheme.typography.bodyMedium,
                        color = MaterialTheme.colorScheme.onSurfaceVariant)
                    else -> LazyColumn(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                        items(kits, key = { it.id }) { kit ->
                            Card(shape = RoundedCornerShape(10.dp), modifier = Modifier.fillMaxWidth()) {
                                Row(Modifier.padding(14.dp), verticalAlignment = Alignment.CenterVertically) {
                                    Icon(Icons.Default.PictureAsPdf, null, tint = Color(0xFFD32F2F), modifier = Modifier.size(28.dp))
                                    Spacer(Modifier.width(12.dp))
                                    Column(Modifier.weight(1f)) {
                                        Text(kit.title?.takeIf { it.isNotBlank() } ?: (kit.fileName ?: "Repo kit"),
                                            style = MaterialTheme.typography.bodyMedium, fontWeight = FontWeight.Medium)
                                        if (!kit.fileName.isNullOrBlank())
                                            Text(kit.fileName, style = MaterialTheme.typography.labelSmall,
                                                color = MaterialTheme.colorScheme.onSurfaceVariant)
                                    }
                                    Button(
                                        onClick = { download(kit) },
                                        shape = RoundedCornerShape(8.dp),
                                        colors = ButtonDefaults.buttonColors(containerColor = TEAL)
                                    ) {
                                        Icon(Icons.Default.Download, null, Modifier.size(18.dp))
                                        Spacer(Modifier.width(6.dp))
                                        Text("Download")
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
    }
}

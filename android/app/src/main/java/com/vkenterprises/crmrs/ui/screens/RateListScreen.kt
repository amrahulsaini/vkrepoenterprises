package com.vkenterprises.crmrs.ui.screens

import android.app.DownloadManager
import android.content.Context
import android.content.Intent
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
import com.vkenterprises.crmrs.data.models.RateListItem
import com.vkenterprises.crmrs.viewmodel.AuthViewModel
import kotlinx.coroutines.flow.first

private val INDIGO = Color(0xFF3F51B5)

private fun RateListItem.isLink() = kind.equals("link", true)

private fun iconFor(item: RateListItem): androidx.compose.ui.graphics.vector.ImageVector {
    if (item.isLink()) return Icons.Default.Link
    val n = (item.fileName ?: "").lowercase()
    return when {
        n.endsWith(".pdf") -> Icons.Default.PictureAsPdf
        n.endsWith(".xls") || n.endsWith(".xlsx") || n.endsWith(".csv") -> Icons.Default.TableChart
        n.endsWith(".doc") || n.endsWith(".docx") -> Icons.Default.Article
        n.endsWith(".ppt") || n.endsWith(".pptx") -> Icons.Default.Slideshow
        n.endsWith(".jpg") || n.endsWith(".jpeg") || n.endsWith(".png") ||
        n.endsWith(".webp") || n.endsWith(".gif") -> Icons.Default.Image
        n.endsWith(".zip") -> Icons.Default.FolderZip
        else -> Icons.Default.InsertDriveFile
    }
}

private fun tintFor(item: RateListItem): Color {
    if (item.isLink()) return INDIGO
    val n = (item.fileName ?: "").lowercase()
    return when {
        n.endsWith(".pdf") -> Color(0xFFD32F2F)
        n.endsWith(".xls") || n.endsWith(".xlsx") || n.endsWith(".csv") -> Color(0xFF2E7D32)
        n.endsWith(".doc") || n.endsWith(".docx") -> Color(0xFF1565C0)
        n.endsWith(".ppt") || n.endsWith(".pptx") -> Color(0xFFEF6C00)
        else -> Color(0xFF546E7A)
    }
}

private fun sizeLabel(bytes: Long): String = when {
    bytes <= 0        -> ""
    bytes < 1024      -> "$bytes B"
    bytes < 1_048_576 -> "${bytes / 1024} KB"
    else              -> String.format("%.1f MB", bytes / 1_048_576.0)
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun RateListScreen(vm: AuthViewModel, nav: NavController, yard: Boolean = false) {
    val context = LocalContext.current

    var items   by remember { mutableStateOf<List<RateListItem>>(emptyList()) }
    var loading by remember { mutableStateOf(true) }
    var error   by remember { mutableStateOf<String?>(null) }
    var query   by remember { mutableStateOf("") }

    LaunchedEffect(Unit) {
        loading = true
        runCatching {
            val uid = vm.userId.first()
            val r = if (yard) ApiClient.api.getYardList(uid) else ApiClient.api.getRateList(uid)
            if (r.isSuccessful) items = r.body().orEmpty()
            else error = if (yard) "Couldn't load the yard list." else "Couldn't load the rate list."
        }.onFailure { error = "Couldn't reach the server." }
        loading = false
    }

    fun open(item: RateListItem) {
        val url = item.url
        if (url.isNullOrBlank()) {
            Toast.makeText(context, "This entry has nothing attached.", Toast.LENGTH_SHORT).show()
            return
        }
        if (item.isLink()) {
            runCatching { context.startActivity(Intent(Intent.ACTION_VIEW, Uri.parse(url))) }
                .onFailure { Toast.makeText(context, "Couldn't open that link.", Toast.LENGTH_SHORT).show() }
            return
        }
        val name = item.fileName?.takeIf { it.isNotBlank() }
            ?: (item.title.replace(Regex("[^A-Za-z0-9._-]"), "_"))
        runCatching {
            val dm = context.getSystemService(Context.DOWNLOAD_SERVICE) as DownloadManager
            val req = DownloadManager.Request(Uri.parse(url))
                .setTitle(name)
                .setDescription("${if (yard) "Yard list" else "Rate list"} — ${item.title}")
                .setNotificationVisibility(DownloadManager.Request.VISIBILITY_VISIBLE_NOTIFY_COMPLETED)
                .setDestinationInExternalPublicDir(Environment.DIRECTORY_DOWNLOADS, name)
            item.mime?.takeIf { it.isNotBlank() }?.let { req.setMimeType(it) }
            dm.enqueue(req)
            Toast.makeText(context, "Downloading $name…", Toast.LENGTH_SHORT).show()
        }.onFailure {
            Toast.makeText(context, "Couldn't start the download.", Toast.LENGTH_SHORT).show()
        }
    }

    val shown = remember(items, query) {
        val q = query.trim().lowercase()
        if (q.isEmpty()) items
        else items.filter {
            it.title.lowercase().contains(q) ||
            (it.financeName ?: "").lowercase().contains(q) ||
            (it.notes ?: "").lowercase().contains(q) ||
            (it.fileName ?: "").lowercase().contains(q)
        }
    }

    Scaffold(
        topBar = {
            TopAppBar(
                title = { Text(if (yard) "Yard List" else "Rate List", fontWeight = FontWeight.Bold) },
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
                label = { Text(if (yard) "Search yard list" else "Search rate list") },
                leadingIcon = { Icon(Icons.Default.Search, null) },
                trailingIcon = {
                    if (query.isNotEmpty())
                        IconButton(onClick = { query = "" }) { Icon(Icons.Default.Close, "Clear") }
                },
                singleLine = true,
                shape = RoundedCornerShape(10.dp),
                modifier = Modifier.fillMaxWidth()
            )
            Spacer(Modifier.height(12.dp))

            when {
                loading -> Box(Modifier.fillMaxWidth().padding(40.dp), contentAlignment = Alignment.Center) {
                    CircularProgressIndicator()
                }
                error != null -> Text(error!!,
                    color = MaterialTheme.colorScheme.error,
                    style = MaterialTheme.typography.bodyMedium)
                shown.isEmpty() -> Column(
                    Modifier.fillMaxWidth().padding(32.dp),
                    horizontalAlignment = Alignment.CenterHorizontally
                ) {
                    Icon(Icons.Default.Assignment, null,
                        tint = MaterialTheme.colorScheme.outlineVariant,
                        modifier = Modifier.size(48.dp))
                    Spacer(Modifier.height(10.dp))
                    Text(
                        if (items.isEmpty()) if (yard) "The office hasn't published a yard list yet." else "The office hasn't published a rate list yet."
                        else "Nothing matches that search.",
                        style = MaterialTheme.typography.bodyMedium,
                        color = MaterialTheme.colorScheme.onSurfaceVariant
                    )
                }
                else -> LazyColumn(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                    items(shown, key = { it.id }) { item ->
                        Card(
                            onClick = { open(item) },
                            shape = RoundedCornerShape(10.dp),
                            modifier = Modifier.fillMaxWidth()
                        ) {
                            Row(Modifier.padding(14.dp), verticalAlignment = Alignment.CenterVertically) {
                                Icon(iconFor(item), null,
                                    tint = tintFor(item), modifier = Modifier.size(28.dp))
                                Spacer(Modifier.width(12.dp))
                                Column(Modifier.weight(1f)) {
                                    Text(item.title,
                                        style = MaterialTheme.typography.bodyMedium,
                                        fontWeight = FontWeight.Bold)
                                    val sub = listOfNotNull(
                                        item.financeName?.takeIf { it.isNotBlank() },
                                        if (item.isLink()) item.url
                                        else item.fileName?.takeIf { it.isNotBlank() },
                                        sizeLabel(item.fileSize).takeIf { it.isNotBlank() }
                                    ).joinToString("  ·  ")
                                    if (sub.isNotBlank())
                                        Text(sub,
                                            style = MaterialTheme.typography.labelSmall,
                                            color = MaterialTheme.colorScheme.onSurfaceVariant,
                                            maxLines = 2)
                                    if (!item.notes.isNullOrBlank())
                                        Text(item.notes,
                                            style = MaterialTheme.typography.labelSmall,
                                            color = MaterialTheme.colorScheme.outline,
                                            maxLines = 2)
                                }
                                Spacer(Modifier.width(8.dp))
                                Icon(
                                    if (item.isLink()) Icons.Default.OpenInNew else Icons.Default.Download,
                                    if (item.isLink()) "Open link" else "Download",
                                    tint = INDIGO, modifier = Modifier.size(22.dp)
                                )
                            }
                        }
                    }
                }
            }
        }
    }
}

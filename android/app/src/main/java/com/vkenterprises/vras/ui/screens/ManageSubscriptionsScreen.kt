package com.vkenterprises.vras.ui.screens

import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.*
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.*
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.*
import androidx.navigation.NavController
import com.vkenterprises.vras.data.models.AdminUserItem
import com.vkenterprises.vras.data.models.SubscriptionRecord
import com.vkenterprises.vras.viewmodel.ManageSubscriptionsViewModel

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun ManageSubscriptionsScreen(
    vm: ManageSubscriptionsViewModel,
    userId: Long,
    nav: NavController
) {
    LaunchedEffect(userId) { vm.init(userId) }

    val ui by vm.ui.collectAsState()

    if (ui.errorMsg != null) {
        LaunchedEffect(ui.errorMsg) {
            kotlinx.coroutines.delay(3000)
            vm.dismissError()
        }
    }

    Scaffold(
        topBar = {
            TopAppBar(
                title = { Text("Manage Subscriptions", fontWeight = FontWeight.Bold) },
                navigationIcon = {
                    if (!ui.passwordGate && ui.selectedUser != null) {
                        IconButton(onClick = { vm.clearUser() }) {
                            Icon(Icons.Default.ArrowBack, contentDescription = "Back")
                        }
                    } else {
                        IconButton(onClick = { nav.popBackStack() }) {
                            Icon(Icons.Default.ArrowBack, contentDescription = "Back")
                        }
                    }
                }
            )
        },
        snackbarHost = {
            if (ui.errorMsg != null) {
                Snackbar(modifier = Modifier.padding(12.dp)) { Text(ui.errorMsg!!) }
            }
        }
    ) { pad ->
        Box(Modifier.padding(pad).fillMaxSize()) {
            when {
                ui.passwordGate -> PasswordGateSection(ui.passwordInput, ui.passwordError, ui.verifying,
                    vm::onPasswordChange, vm::verifyPassword)
                ui.selectedUser != null -> UserSubsSection(
                    user = ui.selectedUser!!,
                    subs = ui.subs,
                    loading = ui.subsLoading,
                    onAddClick = vm::showAddDialog
                ) { vm.deleteSubscription(it) }
                else -> UserListSection(
                    users = ui.users,
                    loading = ui.usersLoading,
                    filter = ui.filterText,
                    onFilterChange = vm::onFilterChange,
                    onUserClick = vm::selectUser
                )
            }
        }
    }

    if (ui.showAddDialog) {
        AddSubscriptionDialog(
            startDate = ui.addStartDate,
            endDate   = ui.addEndDate,
            amount    = ui.addAmount,
            notes     = ui.addNotes,
            error     = ui.addError,
            adding    = ui.adding,
            onStartDate = vm::onAddStartDate,
            onEndDate   = vm::onAddEndDate,
            onAmount    = vm::onAddAmount,
            onNotes     = vm::onAddNotes,
            onConfirm   = vm::addSubscription,
            onDismiss   = vm::hideAddDialog
        )
    }
}

@Composable
private fun PasswordGateSection(
    password: String,
    error: String?,
    verifying: Boolean,
    onPasswordChange: (String) -> Unit,
    onVerify: () -> Unit
) {
    Box(Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
        Column(
            Modifier.fillMaxWidth().padding(40.dp),
            horizontalAlignment = Alignment.CenterHorizontally,
            verticalArrangement = Arrangement.spacedBy(16.dp)
        ) {
            Surface(
                shape = RoundedCornerShape(32.dp),
                color = MaterialTheme.colorScheme.primaryContainer,
                modifier = Modifier.size(80.dp)
            ) {
                Box(contentAlignment = Alignment.Center) {
                    Icon(Icons.Default.Lock, null, Modifier.size(40.dp),
                        tint = MaterialTheme.colorScheme.onPrimaryContainer)
                }
            }

            Text("Admin Access Required", style = MaterialTheme.typography.titleLarge, fontWeight = FontWeight.Bold)
            Text("Enter the subscription management password to continue.",
                style = MaterialTheme.typography.bodyMedium,
                color = MaterialTheme.colorScheme.onSurfaceVariant)

            OutlinedTextField(
                value = password,
                onValueChange = onPasswordChange,
                label = { Text("Subscription Password (SubsPass)") },
                visualTransformation = PasswordVisualTransformation(),
                keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Password),
                singleLine = true,
                isError = error != null,
                supportingText = error?.let { { Text(it, color = MaterialTheme.colorScheme.error) } },
                modifier = Modifier.fillMaxWidth()
            )

            Button(
                onClick = onVerify,
                enabled = !verifying,
                modifier = Modifier.fillMaxWidth().height(50.dp),
                shape = RoundedCornerShape(10.dp)
            ) {
                if (verifying) CircularProgressIndicator(Modifier.size(20.dp), strokeWidth = 2.dp,
                    color = MaterialTheme.colorScheme.onPrimary)
                else Text("UNLOCK", fontWeight = FontWeight.Bold)
            }
        }
    }
}

@Composable
private fun UserListSection(
    users: List<AdminUserItem>,
    loading: Boolean,
    filter: String,
    onFilterChange: (String) -> Unit,
    onUserClick: (AdminUserItem) -> Unit
) {
    Column(Modifier.fillMaxSize()) {
        OutlinedTextField(
            value = filter,
            onValueChange = onFilterChange,
            placeholder = { Text("Search by name or mobile") },
            leadingIcon = { Icon(Icons.Default.Search, null) },
            singleLine = true,
            modifier = Modifier.fillMaxWidth().padding(12.dp),
            shape = RoundedCornerShape(8.dp)
        )
        if (loading) {
            Box(Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
                CircularProgressIndicator()
            }
        } else {
            val filtered = if (filter.isBlank()) users
            else users.filter {
                it.name.contains(filter, ignoreCase = true) ||
                it.mobile.contains(filter, ignoreCase = true)
            }
            LazyColumn {
                items(filtered) { user ->
                    UserRow(user, onUserClick)
                    HorizontalDivider()
                }
            }
        }
    }
}

@Composable
private fun UserRow(user: AdminUserItem, onClick: (AdminUserItem) -> Unit) {
    Row(
        Modifier
            .fillMaxWidth()
            .clickable { onClick(user) }
            .padding(horizontal = 16.dp, vertical = 12.dp),
        verticalAlignment = Alignment.CenterVertically
    ) {
        Column(Modifier.weight(1f)) {
            Text(user.name, fontWeight = FontWeight.Medium, maxLines = 1, overflow = TextOverflow.Ellipsis)
            Text(user.mobile, style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant)
            if (user.subEnd != null) {
                Text("Sub until: ${user.subEnd}", style = MaterialTheme.typography.labelSmall,
                    color = MaterialTheme.colorScheme.primary)
            }
        }
        Icon(Icons.Default.ChevronRight, null, tint = MaterialTheme.colorScheme.outlineVariant)
    }
}

@Composable
private fun UserSubsSection(
    user: AdminUserItem,
    subs: List<SubscriptionRecord>,
    loading: Boolean,
    onAddClick: () -> Unit,
    onDelete: (Long) -> Unit
) {
    Column(Modifier.fillMaxSize()) {
        // User header
        Surface(color = MaterialTheme.colorScheme.primaryContainer) {
            Row(
                Modifier.fillMaxWidth().padding(12.dp),
                verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.SpaceBetween
            ) {
                Column {
                    Text(user.name, fontWeight = FontWeight.Bold)
                    Text(user.mobile, style = MaterialTheme.typography.bodySmall,
                        color = MaterialTheme.colorScheme.onPrimaryContainer.copy(alpha = 0.7f))
                }
                Button(onClick = onAddClick, shape = RoundedCornerShape(8.dp)) {
                    Icon(Icons.Default.Add, null, Modifier.size(16.dp))
                    Spacer(Modifier.width(4.dp))
                    Text("Add")
                }
            }
        }

        if (loading) {
            Box(Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
                CircularProgressIndicator()
            }
        } else if (subs.isEmpty()) {
            Box(Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
                Column(horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.spacedBy(8.dp)) {
                    Icon(Icons.Default.CardMembership, null, Modifier.size(48.dp),
                        tint = MaterialTheme.colorScheme.outlineVariant)
                    Text("No subscriptions found", color = MaterialTheme.colorScheme.onSurfaceVariant)
                }
            }
        } else {
            LazyColumn(Modifier.fillMaxSize(), contentPadding = PaddingValues(8.dp),
                verticalArrangement = Arrangement.spacedBy(8.dp)) {
                items(subs) { sub ->
                    SubscriptionCard(sub, onDelete)
                }
            }
        }
    }
}

@Composable
private fun SubscriptionCard(sub: SubscriptionRecord, onDelete: (Long) -> Unit) {
    var showConfirm by remember { mutableStateOf(false) }
    Card(
        shape = RoundedCornerShape(10.dp),
        colors = CardDefaults.cardColors(
            containerColor = if (sub.isActive) MaterialTheme.colorScheme.primaryContainer
                             else MaterialTheme.colorScheme.surfaceVariant
        )
    ) {
        Row(
            Modifier.fillMaxWidth().padding(12.dp),
            verticalAlignment = Alignment.Top,
            horizontalArrangement = Arrangement.SpaceBetween
        ) {
            Column(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(4.dp)) {
                Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                    Text("${sub.startDate} → ${sub.endDate}", fontWeight = FontWeight.SemiBold,
                        style = MaterialTheme.typography.bodyMedium)
                    if (sub.isActive) {
                        Surface(shape = RoundedCornerShape(4.dp), color = MaterialTheme.colorScheme.primary) {
                            Text("ACTIVE", modifier = Modifier.padding(horizontal = 6.dp, vertical = 2.dp),
                                style = MaterialTheme.typography.labelSmall,
                                color = MaterialTheme.colorScheme.onPrimary, fontWeight = FontWeight.Bold)
                        }
                    }
                }
                Text("Amount: ₹%.2f".format(sub.amount), style = MaterialTheme.typography.bodySmall)
                if (!sub.notes.isNullOrBlank()) {
                    Text(sub.notes, style = MaterialTheme.typography.bodySmall,
                        color = MaterialTheme.colorScheme.onSurfaceVariant, maxLines = 2,
                        overflow = TextOverflow.Ellipsis)
                }
            }
            IconButton(onClick = { showConfirm = true }) {
                Icon(Icons.Default.DeleteOutline, null, tint = MaterialTheme.colorScheme.error)
            }
        }
    }

    if (showConfirm) {
        AlertDialog(
            onDismissRequest = { showConfirm = false },
            title = { Text("Delete Subscription") },
            text  = { Text("Are you sure you want to delete this subscription record?") },
            confirmButton = {
                TextButton(onClick = { showConfirm = false; onDelete(sub.id) }) {
                    Text("DELETE", color = MaterialTheme.colorScheme.error, fontWeight = FontWeight.Bold)
                }
            },
            dismissButton = {
                TextButton(onClick = { showConfirm = false }) { Text("CANCEL") }
            }
        )
    }
}

@Composable
private fun AddSubscriptionDialog(
    startDate: String,
    endDate: String,
    amount: String,
    notes: String,
    error: String?,
    adding: Boolean,
    onStartDate: (String) -> Unit,
    onEndDate: (String) -> Unit,
    onAmount: (String) -> Unit,
    onNotes: (String) -> Unit,
    onConfirm: () -> Unit,
    onDismiss: () -> Unit
) {
    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text("Add Subscription") },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(10.dp)) {
                OutlinedTextField(
                    value = startDate,
                    onValueChange = onStartDate,
                    label = { Text("Start Date (YYYY-MM-DD)") },
                    singleLine = true,
                    modifier = Modifier.fillMaxWidth()
                )
                OutlinedTextField(
                    value = endDate,
                    onValueChange = onEndDate,
                    label = { Text("End Date (YYYY-MM-DD)") },
                    singleLine = true,
                    modifier = Modifier.fillMaxWidth()
                )
                OutlinedTextField(
                    value = amount,
                    onValueChange = onAmount,
                    label = { Text("Amount (₹)") },
                    singleLine = true,
                    keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Decimal),
                    modifier = Modifier.fillMaxWidth()
                )
                OutlinedTextField(
                    value = notes,
                    onValueChange = onNotes,
                    label = { Text("Notes (optional)") },
                    singleLine = true,
                    modifier = Modifier.fillMaxWidth()
                )
                if (error != null) {
                    Text(error, color = MaterialTheme.colorScheme.error,
                        style = MaterialTheme.typography.bodySmall)
                }
            }
        },
        confirmButton = {
            Button(onClick = onConfirm, enabled = !adding) {
                if (adding) CircularProgressIndicator(Modifier.size(16.dp), strokeWidth = 2.dp,
                    color = MaterialTheme.colorScheme.onPrimary)
                else Text("ADD")
            }
        },
        dismissButton = {
            TextButton(onClick = onDismiss, enabled = !adding) { Text("CANCEL") }
        }
    )
}

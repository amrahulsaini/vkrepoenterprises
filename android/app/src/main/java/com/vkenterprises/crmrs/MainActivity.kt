package com.vkenterprises.crmrs

import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.compose.runtime.*
import androidx.hilt.navigation.compose.hiltViewModel
import androidx.navigation.NavType
import androidx.navigation.compose.*
import androidx.navigation.navArgument

import com.vkenterprises.crmrs.navigation.Screen
import com.vkenterprises.crmrs.ui.screens.*
import com.vkenterprises.crmrs.ui.theme.VKTheme
import com.vkenterprises.crmrs.viewmodel.AuthViewModel
import com.vkenterprises.crmrs.viewmodel.ManageSubscriptionsViewModel
import com.vkenterprises.crmrs.viewmodel.ProfileViewModel
import com.vkenterprises.crmrs.viewmodel.SearchViewModel
import com.vkenterprises.crmrs.viewmodel.SettingsViewModel
import dagger.hilt.android.AndroidEntryPoint

@AndroidEntryPoint
class MainActivity : ComponentActivity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        enableEdgeToEdge()
        setContent {
            VKTheme {
                VKNavHost()
            }
        }
    }
}

@Composable
fun VKNavHost() {
    val navController = rememberNavController()
    val authVm: AuthViewModel     = hiltViewModel()
    val searchVm: SearchViewModel = hiltViewModel()
    val repoVm: com.vkenterprises.crmrs.viewmodel.RepoViewModel = hiltViewModel()

    NavHost(navController = navController, startDestination = Screen.Splash.route) {

        composable(Screen.Splash.route) {
            SplashScreen(authVm) { dest -> navController.navigate(dest) {
                popUpTo(Screen.Splash.route) { inclusive = true }
            }}
        }

        composable(Screen.Login.route) {
            LoginScreen(authVm, navController)
        }

        composable(Screen.Register.route) {
            RegisterScreen(authVm, navController)
        }

        composable(
            Screen.WaitingApproval.route,
            arguments = listOf(navArgument("reason") { type = NavType.StringType })
        ) { back ->
            val reason = back.arguments?.getString("reason") ?: ""
            WaitingApprovalScreen(reason, authVm, navController)
        }

        composable(Screen.Home.route) {
            HomeScreen(searchVm, authVm, repoVm, navController)
        }

        composable(Screen.VehicleDetail.route) {
            VehicleDetailScreen(searchVm, authVm, navController)
        }

        composable(Screen.Confirm.route) {
            ConfirmScreen(searchVm, authVm, navController)
        }

        composable(Screen.SubscriptionExpired.route) {
            SubscriptionExpiredScreen(authVm, navController)
        }

        composable(Screen.AppStopped.route) {
            val ui         by searchVm.ui.collectAsState()
            val kickReason by authVm.kickReason.collectAsState()
            LaunchedEffect(kickReason) {
                if (kickReason == "running") {
                    navController.navigate(Screen.Home.route) { popUpTo(0) { inclusive = true } }
                }
            }
            AppStoppedScreen(
                msg = ui.appStoppedMsg.ifBlank { "Your app has been stopped by admin. Please contact agency to start app." },
                vm = authVm, nav = navController
            )
        }

        composable(Screen.Blacklisted.route) {
            val ui         by searchVm.ui.collectAsState()
            val kickReason by authVm.kickReason.collectAsState()
            LaunchedEffect(kickReason) {
                if (kickReason == "running") {
                    navController.navigate(Screen.Home.route) { popUpTo(0) { inclusive = true } }
                }
            }
            BlacklistedScreen(
                msg = ui.blacklistedMsg.ifBlank { "You have been blocked by the agency. Please contact the agency for assistance." },
                vm = authVm, nav = navController
            )
        }

        composable(Screen.Inactive.route) {
            val ui by searchVm.ui.collectAsState()
            InactiveScreen(
                msg = ui.inactiveMsg.ifBlank { "Your account is inactive. Please contact agency." },
                vm = authVm, nav = navController
            )
        }

        composable(Screen.KycPending.route)  { KycPendingScreen(authVm, navController) }
        composable(Screen.KycRejected.route) { KycRejectedScreen(authVm, navController) }
        composable(Screen.KycResubmit.route) { KycResubmitScreen(authVm, navController) }

        composable(Screen.ManageSubscriptions.route) {
            val manageVm: ManageSubscriptionsViewModel = hiltViewModel()
            val userId by authVm.userId.collectAsState(initial = -1L)
            ManageSubscriptionsScreen(manageVm, userId, navController)
        }

        composable(Screen.ControlPanel.route) {
            val cpVm: com.vkenterprises.crmrs.viewmodel.ControlPanelViewModel = hiltViewModel()
            val userId by authVm.userId.collectAsState(initial = -1L)
            com.vkenterprises.crmrs.ui.screens.ControlPanelScreen(cpVm, userId, navController)
        }

        composable(Screen.Profile.route) {
            val profileVm: ProfileViewModel = hiltViewModel()
            val userId by authVm.userId.collectAsState(initial = -1L)
            ProfileScreen(profileVm, userId, navController)
        }

        composable(Screen.Settings.route) {
            val settingsVm: SettingsViewModel = hiltViewModel()
            SettingsScreen(settingsVm, searchVm, authVm, navController)
        }

        composable(Screen.LiveUsers.route) {
            val userId by authVm.userId.collectAsState(initial = -1L)
            LiveUsersScreen(userId = userId, navController = navController)
        }

        composable(Screen.RepoType.route) {
            RepoTypeScreen(repoVm, authVm, navController)
        }
        composable(Screen.RepoHeadOffices.route) {
            RepoHeadOfficeScreen(repoVm, authVm, navController)
        }
        composable(Screen.RepoSearch.route) {
            RepoSearchScreen(repoVm, authVm, navController)
        }
        composable(Screen.RepoPreview.route) {
            RepoPreviewScreen(repoVm, authVm, navController)
        }
    }
}

package com.vkenterprises.crmrs.navigation

sealed class Screen(val route: String) {
    object Splash              : Screen("splash")
    object Login               : Screen("login")
    object Register            : Screen("register")
    object WaitingApproval     : Screen("waiting_approval/{reason}") {
        fun go(reason: String) = "waiting_approval/$reason"
    }
    object Home                : Screen("home")
    object VehicleDetail       : Screen("vehicle_detail")
    object SubscriptionExpired : Screen("subscription_expired")
    object AppStopped          : Screen("app_stopped")
    object Blacklisted         : Screen("blacklisted")
    object Inactive            : Screen("inactive")
    object KycPending          : Screen("kyc_pending")
    object KycRejected         : Screen("kyc_rejected")
    object KycResubmit         : Screen("kyc_resubmit")
    object Profile             : Screen("profile")
    object Confirm             : Screen("confirm")
    object OkForRepo           : Screen("ok_for_repo")
    object Settings            : Screen("settings")
    object LiveUsers           : Screen("live_users")
    object ManageSubscriptions : Screen("manage_subscriptions")
    object ControlPanel        : Screen("control_panel")
    object TaskManager         : Screen("task_manager")
    object RepoHeadOffices     : Screen("repo_head_offices")
    object RepoSearch          : Screen("repo_search")
    object RepoPreview         : Screen("repo_preview")
}

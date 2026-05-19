<?php
/**
 * Plugin Name: Hermes Screenshots
 * Description: Receives Hermes desktop screenshots via REST API and displays them with [hermes_screenshot].
 * Version: 1.0.0
 * Author: Hermes
 * Requires at least: 6.0
 * Requires PHP: 7.4
 * Text Domain: hermes-screenshots
 */

declare(strict_types=1);

if (!defined('ABSPATH')) {
    exit;
}

define('HERMES_SCREENSHOTS_VERSION', '1.0.0');
define('HERMES_SCREENSHOTS_PLUGIN_FILE', __FILE__);
define('HERMES_SCREENSHOTS_PLUGIN_DIR', plugin_dir_path(__FILE__));

require_once HERMES_SCREENSHOTS_PLUGIN_DIR . 'includes/class-hermes-rest-controller.php';
require_once HERMES_SCREENSHOTS_PLUGIN_DIR . 'includes/class-hermes-screenshots-plugin.php';

Hermes_Screenshots_Plugin::instance();

<?php

declare(strict_types=1);

/**
 * SPDX-License-Identifier: AGPL-3.0-or-later
 */

namespace OCA\FilesMiddleClick\AppInfo;

use OCA\Files\Event\LoadAdditionalScriptsEvent;
use OCA\Files_Sharing\Event\BeforeTemplateRenderedEvent;
use OCA\FilesMiddleClick\Listener\LoadScriptListener;
use OCP\AppFramework\App;
use OCP\AppFramework\Bootstrap\IBootContext;
use OCP\AppFramework\Bootstrap\IBootstrap;
use OCP\AppFramework\Bootstrap\IRegistrationContext;

class Application extends App implements IBootstrap {
	public const APP_ID = 'files_middleclick';

	public function __construct() {
		parent::__construct(self::APP_ID);
	}

	public function register(IRegistrationContext $context): void {
		// Files app (logged-in users)
		$context->registerEventListener(LoadAdditionalScriptsEvent::class, LoadScriptListener::class);
		// Public share pages
		$context->registerEventListener(BeforeTemplateRenderedEvent::class, LoadScriptListener::class);
	}

	public function boot(IBootContext $context): void {
	}
}
